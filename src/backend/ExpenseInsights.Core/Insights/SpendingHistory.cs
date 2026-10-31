namespace ExpenseInsights.Core.Insights;

/// <summary>
/// The ledger by month, with the period the imports cover: a month only counts towards a baseline
/// when all of it was imported, and a month with no spending in a category is a zero, not a gap.
/// </summary>
public sealed class SpendingHistory
{
    private readonly ILookup<Month, LedgerEntry> _spending;
    private readonly ILookup<Month, LedgerEntry> _income;

    /// <param name="entries">The ledger.</param>
    /// <param name="firstMonth">The first month the imports cover in full.</param>
    /// <param name="dataEnd">The last day the imports cover.</param>
    public SpendingHistory(IReadOnlyList<LedgerEntry> entries, Month firstMonth, DateOnly dataEnd)
    {
        Entries = entries;
        FirstMonth = firstMonth;
        DataEnd = dataEnd;
        _spending = entries.Where(e => e.Flow == Flow.Spending).ToLookup(e => e.Month);
        _income = entries.Where(e => e.Flow == Flow.Income).ToLookup(e => e.Month);
    }

    public IReadOnlyList<LedgerEntry> Entries { get; }

    /// <summary>The first month a period starting on <paramref name="start"/> covers in full.</summary>
    public static Month FirstFullMonth(DateOnly start) => start.Day == 1 ? Month.Of(start) : Month.Of(start).Plus(1);

    public Month FirstMonth { get; }

    public DateOnly DataEnd { get; }

    public Month LastMonth => Month.Of(DataEnd);

    /// <summary>Imported up to its last day.</summary>
    public bool IsComplete(Month month) => month >= FirstMonth && DataEnd >= month.Last;

    public IEnumerable<LedgerEntry> Spending(Month month) => _spending[month];

    public decimal Spent(Month month) => _spending[month].Sum(e => e.Amount);

    /// <summary>Spending in one top-level category; <c>null</c> is the unclassified part.</summary>
    public decimal Spent(Month month, Guid? categoryId) => _spending[month].Where(e => e.CategoryId == categoryId).Sum(e => e.Amount);

    public decimal Income(Month month) => _income[month].Sum(e => e.Amount);

    /// <summary>
    /// The complete months before <paramref name="month"/> a baseline is the median of: at most
    /// <see cref="InsightStatistics.BaselineMonths"/>, oldest first.
    /// </summary>
    public IReadOnlyList<Month> BaselineMonths(Month month) =>
        [.. Enumerable.Range(1, InsightStatistics.BaselineMonths)
            .Select(back => month.Plus(-back))
            .Where(IsComplete)
            .Reverse()];

    /// <summary>The same month in earlier, complete years, oldest first (seasonality needs 13+ months).</summary>
    public IReadOnlyList<Month> SameMonthInEarlierYears(Month month) =>
        [.. Enumerable.Range(1, Math.Max(0, (month.MonthsSince(FirstMonth) / 12)))
            .Select(years => month.Plus(-12 * years))
            .Where(IsComplete)
            .Reverse()];

    /// <summary>A month's spending by day (index 0 = the 1st).</summary>
    public IReadOnlyList<decimal> Daily(Month month)
    {
        var days = new decimal[month.Days];
        foreach (var entry in _spending[month])
        {
            days[entry.Date.Day - 1] += entry.Amount;
        }

        return days;
    }
}
