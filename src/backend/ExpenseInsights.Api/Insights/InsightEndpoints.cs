using ExpenseInsights.Api.Classification;
using ExpenseInsights.Core.Insights;
using ExpenseInsights.Infrastructure.Insights;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api.Insights;

/// <summary>
/// Summaries and insights (plan 06): the month overview, a category, the transactions behind any
/// number, and recurring payments. Everything is computed here from the user's own history; the UI
/// only formats it.
/// </summary>
internal static class InsightEndpoints
{
    /// <summary>Months in the overview's bar chart, the selected one included.</summary>
    public const int HistoryMonths = 6;

    /// <summary>Months in a category's trend.</summary>
    public const int TrendMonths = 12;

    public const int TopMerchants = 10;

    /// <summary>How far ahead the overview lists upcoming recurring payments.</summary>
    public const int UpcomingDays = 31;

    /// <summary>The <c>category</c> filter value for spending without a category.</summary>
    public const string Unclassified = "none";

    public static void MapInsightEndpoints(this IEndpointRouteBuilder api)
    {
        var insights = api.MapGroup("/insights");
        insights.MapGet("/overview", OverviewAsync);
        insights.MapGet("/categories/{id:guid}", CategoryAsync);
        insights.MapGet("/transactions", TransactionsAsync);
        insights.MapGet("/subscriptions", SubscriptionsAsync);
    }

    /// <summary>One month: the headline numbers, ranked insights, categories and what is coming up.</summary>
    /// <param name="month"><c>yyyy-MM</c>; the latest imported month by default.</param>
    /// <param name="data">The user's spending.</param>
    /// <param name="clock">For "upcoming".</param>
    /// <param name="ct">Cancellation.</param>
    private static async Task<IResult> OverviewAsync(string? month, SpendingData data, TimeProvider clock, CancellationToken ct)
    {
        if (await data.LoadAsync(ct) is not { } spending)
        {
            return Results.Ok(OverviewResponse.Empty);
        }

        if (Resolve(month, spending) is not { } selected)
        {
            return InvalidMonth();
        }

        var history = spending.History;
        var context = InsightContext.For(history, selected);
        var spent = history.Spent(selected);
        var comparison = Usual(history, selected);
        var pace = PaceRule.Project(history, selected);
        var categories = history.Spending(selected)
            .GroupBy(e => e.CategoryId)
            .Select(g => new CategoryTotal(g.Key, g.Sum(e => e.Amount), UsualFor(history, selected, g.Key)))
            .Where(c => c.Amount > 0)
            .OrderByDescending(c => c.Amount)
            .ToList();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var upcoming = selected == history.LastMonth
            ? context.Recurring.Where(r => r.IsActive && r.NextDate >= today && r.NextDate.DayNumber - today.DayNumber <= UpcomingDays).ToList()
            : [];

        return Results.Ok(new OverviewResponse(
            true,
            selected.ToString(),
            spending.Currency,
            spending.EarliestMonth.ToString(),
            history.LastMonth.ToString(),
            history.DataEnd,
            context.IsPartial,
            context.End,
            spent,
            history.Income(selected),
            comparison,
            pace?.Projected,
            MonthTotals(spending, selected, HistoryMonths),
            categories,
            InsightEngine.Default.Evaluate(context),
            [.. upcoming.Select(r => SubscriptionResponse.From(r, history))]));
    }

    /// <summary>A top-level category: its month against the usual, its trend and its merchants.</summary>
    private static async Task<IResult> CategoryAsync(Guid id, string? month, SpendingData data, AppDbContext db, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (category is null || category.ParentId is not null)
        {
            return ApiProblem.Of(StatusCodes.Status404NotFound, "categoryNotFound", "There is no such top-level category.");
        }

        if (await data.LoadAsync(ct) is not { } spending)
        {
            return Results.Ok(new CategoryResponse(id, null, SpendingData.DefaultCurrency, false, 0, null, [], []));
        }

        if (Resolve(month, spending) is not { } selected)
        {
            return InvalidMonth();
        }

        var history = spending.History;
        var merchants = history.Spending(selected)
            .Where(e => e.CategoryId == id)
            .GroupBy(e => e.MerchantKey)
            .Select(g => new MerchantTotal(g.Key, g.GroupBy(e => e.Merchant).OrderByDescending(n => n.Count()).First().Key, g.Sum(e => e.Amount), g.Select(e => e.TransactionId).Distinct().Count()))
            .OrderByDescending(m => m.Amount)
            .Take(TopMerchants)
            .ToList();

        return Results.Ok(new CategoryResponse(
            id,
            selected.ToString(),
            spending.Currency,
            !history.IsComplete(selected),
            history.Spent(selected, id),
            UsualFor(history, selected, id),
            MonthTotals(spending, selected, TrendMonths, id),
            merchants));
    }

    /// <summary>
    /// The transactions behind a number: a month's spending, optionally one category (<c>none</c>
    /// for unclassified) and one merchant, each with the amount it counts for.
    /// </summary>
    private static async Task<IResult> TransactionsAsync(string? month, string? category, string? merchant, SpendingData data, AppDbContext db, CancellationToken ct)
    {
        Guid? categoryId = null;
        if (category is not null && category != Unclassified)
        {
            if (!Guid.TryParse(category, out var parsed))
            {
                return ApiProblem.Of(StatusCodes.Status400BadRequest, "invalidCategory", $"The category is an id or '{Unclassified}'.");
            }

            categoryId = parsed;
        }

        if (await data.LoadAsync(ct) is not { } spending)
        {
            return Results.Ok(new CountedTransactions(month, SpendingData.DefaultCurrency, 0, []));
        }

        if (Resolve(month, spending) is not { } selected)
        {
            return InvalidMonth();
        }

        var counted = spending.History.Spending(selected)
            .Where(e => category is null || e.CategoryId == categoryId)
            .Where(e => merchant is null || e.MerchantKey == merchant)
            .GroupBy(e => e.TransactionId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        var ids = counted.Keys.ToList();
        var items = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.Id)
            .Select(TransactionItem.Projection)
            .ToListAsync(ct);

        return Results.Ok(new CountedTransactions(
            selected.ToString(),
            spending.Currency,
            counted.Values.Sum(),
            [.. items.Select(i => new CountedTransaction(i, counted[i.Id]))]));
    }

    /// <summary>Recurring payments as of the last import: active first, with what they cost a month and a year.</summary>
    private static async Task<IResult> SubscriptionsAsync(SpendingData data, CancellationToken ct)
    {
        if (await data.LoadAsync(ct) is not { } spending)
        {
            return Results.Ok(new SubscriptionsResponse(SpendingData.DefaultCurrency, 0, 0, []));
        }

        var history = spending.History;
        var context = InsightContext.For(history, history.LastMonth);
        var all = context.Recurring
            .OrderByDescending(r => r.IsActive)
            .ThenByDescending(r => r.MonthlyCost)
            .Select(r => SubscriptionResponse.From(r, history, context))
            .ToList();
        var active = context.Recurring.Where(r => r.IsActive).ToList();
        return Results.Ok(new SubscriptionsResponse(spending.Currency, active.Sum(r => r.MonthlyCost), active.Sum(r => r.YearlyCost), all));
    }

    /// <summary>The selected month, or the latest; <c>null</c> when it isn't a month the user has data for.</summary>
    private static Month? Resolve(string? month, UserSpending spending)
    {
        if (string.IsNullOrEmpty(month))
        {
            return spending.History.LastMonth;
        }

        return Month.TryParse(month, out var parsed) && parsed >= spending.EarliestMonth && parsed <= spending.History.LastMonth ? parsed : null;
    }

    /// <summary>
    /// The month against the usual: the baseline median for a complete month, what is usually spent
    /// by this day for one in progress. <c>null</c> without enough history.
    /// </summary>
    private static UsualResponse? Usual(SpendingHistory history, Month month)
    {
        var spent = history.Spent(month);
        if (PaceRule.Project(history, month) is { } pace)
        {
            // Unusual when the projected month is (the pace rule's test).
            var projected = InsightStatistics.Compare(pace.Projected, pace.Baseline);
            return new UsualResponse(pace.UsualByNow, pace.Baseline.Months, Change(spent, pace.UsualByNow), projected.IsUnusual, true);
        }

        if (history.IsComplete(month) && InsightStatistics.Baseline([.. history.BaselineMonths(month).Select(history.Spent)]) is { } baseline)
        {
            return new UsualResponse(baseline.Median, baseline.Months, Change(spent, baseline.Median), InsightStatistics.Compare(spent, baseline).IsUnusual, false);
        }

        return null;
    }

    /// <summary>A category's baseline median, if it has one.</summary>
    private static decimal? UsualFor(SpendingHistory history, Month month, Guid? categoryId) =>
        InsightStatistics.Baseline([.. history.BaselineMonths(month).Select(m => history.Spent(m, categoryId))])?.Median;

    /// <summary>Spending (all, or one category) for the months up to the selected one, oldest first, from the first imported.</summary>
    private static List<MonthTotal> MonthTotals(UserSpending spending, Month month, int count, Guid? categoryId = null) =>
        [.. Enumerable.Range(0, count)
            .Select(back => month.Plus(-back))
            .Where(m => m >= spending.EarliestMonth)
            .Reverse()
            .Select(m => new MonthTotal(
                m.ToString(),
                categoryId is null ? spending.History.Spent(m) : spending.History.Spent(m, categoryId),
                spending.History.IsComplete(m)))];

    private static decimal? Change(decimal amount, decimal usual) => usual == 0 ? null : Math.Round((amount - usual) / usual, 4);

    private static IResult InvalidMonth() =>
        ApiProblem.Of(StatusCodes.Status400BadRequest, "invalidMonth", "The month is yyyy-MM, within the imported history.");
}

/// <remarks>
/// <para><c>HasData</c>: <c>false</c> before the first import; everything else is empty then.</para>
/// <para><c>EarliestMonth</c>, <c>LatestMonth</c>: The range the month switcher offers.</para>
/// <para><c>DataEnd</c>: The last imported day.</para>
/// <para><c>IsPartial</c>: The month isn't over (or not imported to its end).</para>
/// <para><c>Through</c>: The last day the month's numbers cover.</para>
/// <para><c>Usual</c>: The comparison for the headline; <c>null</c> without 3 months of history.</para>
/// <para><c>Projected</c>: For a month in progress: the whole month at the usual pace.</para>
/// <para><c>History</c>: Spending in the last <see cref="InsightEndpoints.HistoryMonths"/> months, oldest first.</para>
/// <para><c>Categories</c>: Spending by top-level category, largest first; a <c>null</c> id is unclassified.</para>
/// <para><c>Insights</c>: Ranked: up to three, then the uncategorized reminder.</para>
/// <para><c>Upcoming</c>: Recurring payments due in the next month (latest month only).</para>
/// </remarks>
internal sealed record OverviewResponse(
    bool HasData,
    string? Month,
    string Currency,
    string? EarliestMonth,
    string? LatestMonth,
    DateOnly? DataEnd,
    bool IsPartial,
    DateOnly? Through,
    decimal Spent,
    decimal Income,
    UsualResponse? Usual,
    decimal? Projected,
    IReadOnlyList<MonthTotal> History,
    IReadOnlyList<CategoryTotal> Categories,
    IReadOnlyList<Insight> Insights,
    IReadOnlyList<SubscriptionResponse> Upcoming)
{
    public static readonly OverviewResponse Empty = new(false, null, SpendingData.DefaultCurrency, null, null, null, false, null, 0, 0, null, null, [], [], [], []);
}

/// <remarks>
/// <para><c>Amount</c>: The usual amount: the median of the previous months, or (by day) what is usually spent by now.</para>
/// <para><c>Change</c>: (spent − usual) ÷ usual.</para>
/// <para><c>IsUnusual</c>: The difference clears the significance threshold (median/MAD rule).</para>
/// <para><c>ByDay</c>: Compared up to the same day of the month.</para>
/// </remarks>
internal sealed record UsualResponse(decimal Amount, int Months, decimal? Change, bool IsUnusual, bool ByDay);

/// <remarks>
/// <para><c>IsComplete</c>: The whole month is imported.</para>
/// </remarks>
internal sealed record MonthTotal(string Month, decimal Spent, bool IsComplete);

/// <remarks>
/// <para><c>Usual</c>: The category's baseline median.</para>
/// </remarks>
internal sealed record CategoryTotal(Guid? CategoryId, decimal Amount, decimal? Usual);

internal sealed record MerchantTotal(string MerchantKey, string Merchant, decimal Amount, int Count);

/// <remarks>
/// <para><c>Trend</c>: The last <see cref="InsightEndpoints.TrendMonths"/> months, oldest first.</para>
/// <para><c>Merchants</c>: This month's largest, at most <see cref="InsightEndpoints.TopMerchants"/>.</para>
/// </remarks>
internal sealed record CategoryResponse(
    Guid CategoryId,
    string? Month,
    string Currency,
    bool IsPartial,
    decimal Spent,
    decimal? Usual,
    IReadOnlyList<MonthTotal> Trend,
    IReadOnlyList<MerchantTotal> Merchants);

/// <remarks>
/// <para><c>Counted</c>: What it adds to the number (spending positive, a refund negative); part of it for a split.</para>
/// </remarks>
internal sealed record CountedTransaction(TransactionItem Transaction, decimal Counted);

internal sealed record CountedTransactions(string? Month, string Currency, decimal Total, IReadOnlyList<CountedTransaction> Items);

/// <remarks>
/// <para><c>IsNew</c>: First detected in the latest month, and started after the imports did.</para>
/// </remarks>
internal sealed record SubscriptionResponse(
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
    bool IsActive,
    decimal MonthlyCost,
    decimal YearlyCost,
    bool PriceChanged,
    bool IsNew)
{
    public static SubscriptionResponse From(RecurringPayment r, SpendingHistory history, InsightContext? context = null)
    {
        var isNew = context is not null
            && context.Month.Contains(r.LastDate)
            && !context.RecurringBefore.Any(b => b.MerchantKey == r.MerchantKey)
            && SubscriptionRule.IsNew(r, history);
        return new(r.MerchantKey, r.Merchant, r.CategoryId, r.CadenceDays, r.LatestAmount, r.PreviousAmount, r.FirstDate, r.LastDate, r.NextDate, r.Occurrences, r.IsActive, r.MonthlyCost, r.YearlyCost, r.PriceChanged, isNew);
    }
}

internal sealed record SubscriptionsResponse(string Currency, decimal MonthlyTotal, decimal YearlyTotal, IReadOnlyList<SubscriptionResponse> Items);
