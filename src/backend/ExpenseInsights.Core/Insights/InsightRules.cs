namespace ExpenseInsights.Core.Insights;

/// <summary>What a rule sees: the user's history and the month being looked at.</summary>
/// <remarks>
/// <para><c>Recurring</c>: Recurring payments as of the end of the month (or the data, if earlier).</para>
/// <para><c>RecurringBefore</c>: Recurring payments as of the end of the month before.</para>
/// </remarks>
public sealed record InsightContext(
    SpendingHistory History,
    Month Month,
    IReadOnlyList<RecurringPayment> Recurring,
    IReadOnlyList<RecurringPayment> RecurringBefore)
{
    /// <summary>The month isn't over (or not imported to its end): a drop isn't news yet.</summary>
    public bool IsPartial => !History.IsComplete(Month);

    public DateOnly End => IsPartial ? History.DataEnd : Month.Last;

    public static InsightContext For(SpendingHistory history, Month month)
    {
        var end = history.IsComplete(month) || history.DataEnd < month.First ? month.Last : history.DataEnd;
        return new(
            history,
            month,
            RecurringPayments.Detect(history.Entries, end),
            RecurringPayments.Detect(history.Entries, month.First.AddDays(-1)));
    }
}

/// <summary>One typed insight rule (decision D6): code decides what is notable, never a model.</summary>
public interface IInsightRule
{
    IEnumerable<Insight> Evaluate(InsightContext context);
}

/// <summary>A category's month against the median/MAD of its previous months, and its season.</summary>
public sealed class CategoryChangeRule : IInsightRule
{
    public IEnumerable<Insight> Evaluate(InsightContext context)
    {
        var history = context.History;
        var months = history.BaselineMonths(context.Month);
        var seasons = history.SameMonthInEarlierYears(context.Month);
        var categories = history.Entries
            .Where(e => e.Flow == Flow.Spending && e.CategoryId is not null && (e.Month == context.Month || months.Contains(e.Month)))
            .Select(e => e.CategoryId!.Value)
            .Distinct();
        foreach (var category in categories)
        {
            if (InsightStatistics.Baseline([.. months.Select(m => history.Spent(m, category))]) is not { } baseline)
            {
                yield break;
            }

            var value = history.Spent(context.Month, category);
            var lastYear = InsightStatistics.SeasonalReference([.. seasons.Select(m => history.Spent(m, category))]);
            var comparison = InsightStatistics.CompareWithSeason(value, baseline, lastYear);
            if (!comparison.IsUnusual || (context.IsPartial && !comparison.IsUp))
            {
                continue;
            }

            yield return new Insight(
                InsightType.CategoryChange,
                InsightStatistics.Score(comparison.Z, comparison.Delta),
                comparison.IsUp ? InsightDirection.Up : InsightDirection.Down,
                CategoryId: category,
                Amount: value,
                Usual: baseline.Median,
                Months: baseline.Months,
                LastYear: lastYear);
        }
    }
}

/// <summary>
/// The month so far, projected from the typical share spent by this day (not linearly: rent and
/// bills land early). Only after day 7, and only when the projection is unusual.
/// </summary>
public sealed class PaceRule : IInsightRule
{
    public const int FromDay = 8;

    public IEnumerable<Insight> Evaluate(InsightContext context)
    {
        if (Project(context.History, context.Month) is not { } pace || pace.Day < FromDay)
        {
            yield break;
        }

        var comparison = InsightStatistics.Compare(pace.Projected, pace.Baseline);
        if (comparison.IsUnusual)
        {
            yield return new Insight(
                InsightType.Pace,
                InsightStatistics.Score(comparison.Z, comparison.Delta),
                comparison.IsUp ? InsightDirection.Up : InsightDirection.Down,
                Amount: pace.Projected,
                Usual: pace.Baseline.Median,
                Months: pace.Baseline.Months,
                SoFar: pace.SoFar);
        }
    }

    /// <summary>The projection for a month still in progress; <c>null</c> for a complete one or without a baseline.</summary>
    public static PaceProjection? Project(SpendingHistory history, Month month)
    {
        if (history.IsComplete(month) || history.LastMonth != month)
        {
            return null;
        }

        var months = history.BaselineMonths(month);
        var day = history.DataEnd.Day;
        if (InsightStatistics.Baseline([.. months.Select(history.Spent)]) is not { } baseline
            || InsightStatistics.ShareByDay(months.Select(history.Daily), day) is not { } share
            || share <= 0)
        {
            return null;
        }

        var soFar = history.Spent(month);
        return new PaceProjection(day, soFar, Math.Round(soFar / share, 2), Math.Round(baseline.Median * share, 2), baseline);
    }
}

/// <remarks>
/// <para><c>Day</c>: The last imported day of the month.</para>
/// <para><c>SoFar</c>: Spent up to that day.</para>
/// <para><c>Projected</c>: The whole month at the usual pace from here.</para>
/// <para><c>UsualByNow</c>: What is usually spent by that day: the baseline × the typical share.</para>
/// </remarks>
public sealed record PaceProjection(int Day, decimal SoFar, decimal Projected, decimal UsualByNow, Baseline Baseline);

/// <summary>
/// A payment larger than any in its category over the last 12 months and above 3 × the category's
/// median payment, so a category with five payments doesn't flag its largest every month. (The plan
/// started from the p95; on the fake data a category with ~25 payments a month has one above its
/// p95 every month by definition, so the bar is the 12-month maximum, the p100.)
/// </summary>
public sealed class LargePaymentRule : IInsightRule
{
    public const decimal Percentile = 1m;

    public const decimal MedianMultiple = 3;

    public const int MinimumHistory = 5;

    public IEnumerable<Insight> Evaluate(InsightContext context)
    {
        var from = context.Month.First.AddMonths(-12);
        var spending = context.History.Entries.Where(e => e.Flow == Flow.Spending && e.CategoryId is not null && e.Amount > 0).ToList();
        foreach (var payments in context.History.Spending(context.Month).Where(e => e.CategoryId is not null && e.Amount > 0).GroupBy(e => e.CategoryId))
        {
            var past = spending.Where(e => e.CategoryId == payments.Key && e.Date >= from && e.Date < context.Month.First).Select(e => e.Amount).ToArray();
            if (past.Length < MinimumHistory)
            {
                continue;
            }

            var largest = payments.OrderByDescending(e => e.Amount).ThenBy(e => e.TransactionId).First();
            var median = InsightStatistics.Median(past);
            var comparison = InsightStatistics.Compare(largest.Amount, new Baseline(median, InsightStatistics.Spread(past), past.Length));
            if (largest.Amount > InsightStatistics.Percentile(past, Percentile) && largest.Amount > MedianMultiple * median && comparison.IsUnusual)
            {
                yield return new Insight(
                    InsightType.LargePayment,
                    InsightStatistics.Score(comparison.Z, comparison.Delta),
                    CategoryId: payments.Key,
                    MerchantKey: largest.MerchantKey,
                    Merchant: largest.Merchant,
                    TransactionId: largest.TransactionId,
                    Amount: largest.Amount,
                    Usual: median);
            }
        }
    }
}

/// <summary>
/// Many small payments at one merchant (Żabka): this month's count and sum both unusually high
/// against the merchant's own baseline.
/// </summary>
public sealed class FrequentSmallRule : IInsightRule
{
    /// <summary>A payment is small when the merchant's median payment is at most this.</summary>
    public const decimal SmallPayment = 50;

    /// <summary>Frequent: at least this many payments in a usual month.</summary>
    public const decimal MinimumUsualCount = 3;

    /// <summary>At least this many more payments than usual.</summary>
    public const decimal MinimumExtraCount = 3;

    public IEnumerable<Insight> Evaluate(InsightContext context)
    {
        var history = context.History;
        var months = history.BaselineMonths(context.Month);
        if (months.Count < InsightStatistics.MinimumBaselineMonths)
        {
            yield break;
        }

        var payments = history.Entries
            .Where(e => e.Flow == Flow.Spending && e.Amount > 0 && e.MerchantKey.Length > 0 && (e.Month == context.Month || months.Contains(e.Month)))
            .ToLookup(e => e.MerchantKey);
        foreach (var current in history.Spending(context.Month).Where(e => e.Amount > 0 && e.MerchantKey.Length > 0).GroupBy(e => e.MerchantKey))
        {
            var past = payments[current.Key].Where(e => e.Month != context.Month).ToList();
            if (past.Count == 0 || InsightStatistics.Median(past.Select(e => e.Amount)) > SmallPayment)
            {
                continue;
            }

            var counts = InsightStatistics.Baseline([.. months.Select(m => (decimal)past.Count(e => e.Month == m))])!;
            var sums = InsightStatistics.Baseline([.. months.Select(m => past.Where(e => e.Month == m).Sum(e => e.Amount))])!;
            if (counts.Median < MinimumUsualCount)
            {
                continue;
            }

            var count = current.Count();
            var sum = current.Sum(e => e.Amount);
            var byCount = InsightStatistics.Compare(count, counts, MinimumExtraCount);
            var bySum = InsightStatistics.Compare(sum, sums);
            if (byCount is { IsUnusual: true, IsUp: true } && bySum is { IsUnusual: true, IsUp: true })
            {
                yield return new Insight(
                    InsightType.FrequentSmall,
                    InsightStatistics.Score(Math.Min(byCount.Z, bySum.Z), bySum.Delta),
                    CategoryId: current.Select(e => e.CategoryId).FirstOrDefault(c => c is not null),
                    MerchantKey: current.Key,
                    Merchant: current.GroupBy(e => e.Merchant).OrderByDescending(g => g.Count()).First().Key,
                    Amount: sum,
                    Usual: sums.Median,
                    Months: sums.Months,
                    Count: count,
                    UsualCount: (int)Math.Round(counts.Median, MidpointRounding.AwayFromZero));
            }
        }
    }
}

/// <summary>A recurring payment first detected this month, or one whose price changed this month.</summary>
public sealed class SubscriptionRule : IInsightRule
{
    /// <summary>
    /// It started after the imports did: one that was paid in the first cadence of the history may
    /// be years old, it just wasn't imported yet.
    /// </summary>
    public static bool IsNew(RecurringPayment payment, SpendingHistory history) =>
        payment.FirstDate.DayNumber - history.FirstMonth.First.DayNumber > (1 + RecurringPayments.CadenceTolerance) * payment.CadenceDays;

    public IEnumerable<Insight> Evaluate(InsightContext context)
    {
        var before = context.RecurringBefore.Select(r => r.MerchantKey).ToHashSet(StringComparer.Ordinal);
        foreach (var payment in context.Recurring.Where(r => context.Month.Contains(r.LastDate)))
        {
            if (!before.Contains(payment.MerchantKey) && IsNew(payment, context.History))
            {
                // Just unusual (z = k): ranked by what it costs a year.
                yield return new Insight(
                    InsightType.NewSubscription,
                    InsightStatistics.Score((double)InsightStatistics.K, payment.YearlyCost),
                    CategoryId: payment.CategoryId,
                    MerchantKey: payment.MerchantKey,
                    Merchant: payment.Merchant,
                    Amount: payment.LatestAmount,
                    CadenceDays: payment.CadenceDays);
            }
            else if (payment.PriceChanged)
            {
                var change = payment.LatestAmount - payment.PreviousAmount;
                var z = (double)(Math.Abs(change) / (RecurringPayments.PriceChangeTolerance * payment.PreviousAmount));
                yield return new Insight(
                    InsightType.SubscriptionPriceChange,
                    InsightStatistics.Score(z, change * payment.PeriodsPerYear),
                    change > 0 ? InsightDirection.Up : InsightDirection.Down,
                    CategoryId: payment.CategoryId,
                    MerchantKey: payment.MerchantKey,
                    Merchant: payment.Merchant,
                    Amount: payment.LatestAmount,
                    Usual: payment.PreviousAmount,
                    CadenceDays: payment.CadenceDays);
            }
        }
    }
}

/// <summary>"12 transakcji czeka na kategorię": a nudge, not a statistic.</summary>
public sealed class UncategorizedRule : IInsightRule
{
    public IEnumerable<Insight> Evaluate(InsightContext context)
    {
        var open = context.History.Spending(context.Month).Where(e => e.CategoryId is null && e.Amount > 0).ToList();
        if (open.Count > 0)
        {
            yield return new Insight(
                InsightType.Uncategorized,
                0,
                Amount: open.Sum(e => e.Amount),
                Count: open.Select(e => e.TransactionId).Distinct().Count());
        }
    }
}
