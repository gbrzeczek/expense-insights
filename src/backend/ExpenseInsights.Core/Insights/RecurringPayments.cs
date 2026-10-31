namespace ExpenseInsights.Core.Insights;

/// <summary>A payment that repeats on a regular cadence (a subscription, rent).</summary>
/// <remarks>
/// <para><c>CategoryId</c>: The top-level category most of its payments are in.</para>
/// <para><c>CadenceDays</c>: 7, 30 or 365.</para>
/// <para><c>LatestAmount</c>: The last payment (positive).</para>
/// <para><c>PreviousAmount</c>: The one before it.</para>
/// <para><c>NextDate</c>: When the next one is expected.</para>
/// <para><c>IsActive</c>: The last payment is recent enough for the cadence.</para>
/// </remarks>
public sealed record RecurringPayment(
    string MerchantKey,
    string Merchant,
    Guid? CategoryId,
    int CadenceDays,
    decimal LatestAmount,
    decimal PreviousAmount,
    DateOnly FirstDate,
    DateOnly LastDate,
    DateOnly NextDate,
    int Occurrences,
    bool IsActive)
{
    public decimal MonthlyCost => Math.Round(CadenceDays switch
    {
        7 => LatestAmount * 52 / 12,
        365 => LatestAmount / 12,
        _ => LatestAmount,
    }, 2);

    public decimal YearlyCost => Math.Round(LatestAmount * PeriodsPerYear, 2);

    /// <summary>The latest amount is outside ±2% of the previous one.</summary>
    public bool PriceChanged => Math.Abs(LatestAmount - PreviousAmount) > RecurringPayments.PriceChangeTolerance * PreviousAmount;

    public int PeriodsPerYear => CadenceDays switch
    {
        7 => 52,
        365 => 1,
        _ => 12,
    };
}

/// <summary>
/// Finds recurring payments: the same merchant, a similar amount (±10%), a regular cadence (the
/// median gap is 7, 30 or 365 days ± 20%), at least twice. The latest payment may differ by more
/// (a price change, up to ±50%) when it comes on schedule after at least two at the old price.
/// Only the payments at the current price (and the one before a change) count.
/// </summary>
public static class RecurringPayments
{
    public static readonly int[] Cadences = [7, 30, 365];

    public const decimal AmountTolerance = 0.10m;

    public const double CadenceTolerance = 0.20;

    public const decimal PriceChangeTolerance = 0.02m;

    /// <summary>How far a changed price may move and still be the same subscription.</summary>
    public const decimal PriceChangeLimit = 0.50m;

    public const int MinimumOccurrences = 2;

    /// <summary>A subscription is still active until this many cadences pass without a payment.</summary>
    public const double ActiveFor = 1.5;

    /// <summary>Recurring payments as of <paramref name="asOf"/>, from the spending ledger.</summary>
    public static IReadOnlyList<RecurringPayment> Detect(IEnumerable<LedgerEntry> entries, DateOnly asOf)
    {
        var found = new List<RecurringPayment>();
        var payments = entries.Where(e => e.Flow == Flow.Spending && e.Amount > 0 && e.MerchantKey.Length > 0 && e.Date <= asOf);
        foreach (var merchant in payments.GroupBy(e => e.MerchantKey))
        {
            if (DetectOne([.. merchant.OrderBy(e => e.Date).ThenBy(e => e.TransactionId)], asOf) is { } payment)
            {
                found.Add(payment);
            }
        }

        return [.. found.OrderBy(p => p.NextDate).ThenBy(p => p.MerchantKey, StringComparer.Ordinal)];
    }

    private static RecurringPayment? DetectOne(IReadOnlyList<LedgerEntry> series, DateOnly asOf)
    {
        // Price segments: consecutive payments within ±10% of the one before. A subscription is one
        // segment, or several (each price held at least twice) ending in the current price; a
        // varying bill or a shop breaks into single payments.
        var segments = new List<List<LedgerEntry>>();
        foreach (var payment in series)
        {
            if (segments.Count > 0 && Similar(payment.Amount, segments[^1][^1].Amount, AmountTolerance))
            {
                segments[^1].Add(payment);
            }
            else
            {
                segments.Add([payment]);
            }
        }

        // A latest payment on its own may be a new price, if it comes on schedule.
        var priceChange = segments.Count > 1 && segments[^1].Count == 1;
        var current = priceChange ? segments[^2] : segments[^1];
        var earlier = segments.Take(segments.Count - (priceChange ? 2 : 1));
        var latest = series[^1];
        if (current.Count < MinimumOccurrences
            || earlier.Any(s => s.Count < MinimumOccurrences)
            || (priceChange && !Similar(latest.Amount, current[^1].Amount, PriceChangeLimit)))
        {
            return null;
        }

        List<LedgerEntry> run = priceChange ? [.. current, latest] : current;
        var previous = run[^2];
        var gaps = run.Zip(run.Skip(1), (a, b) => (decimal)(b.Date.DayNumber - a.Date.DayNumber)).ToArray();
        var medianGap = (double)InsightStatistics.Median(gaps);
        var cadence = Cadences.FirstOrDefault(c => Math.Abs(medianGap - c) <= CadenceTolerance * c);
        if (cadence == 0 || (priceChange && Math.Abs((double)gaps[^1] - cadence) > CadenceTolerance * cadence))
        {
            return null;
        }

        var next = cadence switch
        {
            7 => latest.Date.AddDays(7),
            365 => latest.Date.AddYears(1),
            _ => latest.Date.AddMonths(1),
        };
        var category = run.Where(e => e.CategoryId is not null).GroupBy(e => e.CategoryId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var merchantName = run.GroupBy(e => e.Merchant).OrderByDescending(g => g.Count()).First().Key;
        return new RecurringPayment(
            latest.MerchantKey,
            merchantName,
            category,
            cadence,
            latest.Amount,
            previous.Amount,
            run[0].Date,
            latest.Date,
            next,
            run.Count,
            asOf.DayNumber - latest.Date.DayNumber <= ActiveFor * cadence);
    }

    private static bool Similar(decimal amount, decimal reference, decimal tolerance) =>
        Math.Abs(amount - reference) <= tolerance * Math.Abs(reference);
}
