using System.Globalization;

namespace ExpenseInsights.Core.Insights;

/// <summary>A calendar month (<c>2026-10</c>): the unit summaries and insights are computed for.</summary>
public readonly record struct Month(int Year, int Number) : IComparable<Month>
{
    public DateOnly First => new(Year, Number, 1);

    public DateOnly Last => new(Year, Number, Days);

    public int Days => DateTime.DaysInMonth(Year, Number);

    public static Month Of(DateOnly date) => new(date.Year, date.Month);

    public Month Plus(int months)
    {
        var date = First.AddMonths(months);
        return new(date.Year, date.Month);
    }

    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Number;

    /// <summary>Months from <paramref name="other"/> to this one (positive when this one is later).</summary>
    public int MonthsSince(Month other) => ((Year - other.Year) * 12) + Number - other.Number;

    public static bool TryParse(string? text, out Month month)
    {
        if (DateOnly.TryParseExact($"{text}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            month = Of(date);
            return true;
        }

        month = default;
        return false;
    }

    public int CompareTo(Month other) => MonthsSince(other) switch
    {
        > 0 => 1,
        < 0 => -1,
        _ => 0,
    };

    public static bool operator <(Month left, Month right) => left.CompareTo(right) < 0;

    public static bool operator >(Month left, Month right) => left.CompareTo(right) > 0;

    public static bool operator <=(Month left, Month right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Month left, Month right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Year:D4}-{Number:D2}";
}
