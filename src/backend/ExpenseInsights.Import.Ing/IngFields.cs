using System.Globalization;
using System.Text.RegularExpressions;

namespace ExpenseInsights.Import.Ing;

/// <summary>Cleaning and parsing of single ING field values.</summary>
internal static partial class IngFields
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy"];

    /// <summary>Trims, and strips the apostrophes ING wraps ids and account numbers in (<c>'2026…'</c>).</summary>
    public static string Clean(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    public static string? CleanOrNull(string value) => Clean(value) is { Length: > 0 } cleaned ? cleaned : null;

    /// <summary>Trims and collapses runs of whitespace: <c>" Bershka.com     Arteixo "</c> → <c>"Bershka.com Arteixo"</c>.</summary>
    public static string CollapseSpaces(string value) => Whitespace().Replace(value.Trim(), " ");

    /// <summary>Decimal comma, no thousands separator: <c>-298,80</c>. Empty → <c>null</c>.</summary>
    public static bool TryParseAmount(string value, out decimal? amount)
    {
        amount = null;
        var cleaned = Clean(value);
        if (cleaned.Length == 0)
        {
            return true;
        }

        // Spreadsheet re-saves may add a (non-breaking) space as a thousands separator.
        cleaned = cleaned.Replace(" ", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace(',', '.');
        if (decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            amount = parsed;
            return true;
        }

        return false;
    }

    /// <summary><c>yyyy-MM-dd</c> as exported, or <c>dd.MM.yyyy</c> after a spreadsheet re-save. Empty → <c>null</c>.</summary>
    public static bool TryParseDate(string value, out DateOnly? date)
    {
        date = null;
        var cleaned = Clean(value);
        if (cleaned.Length == 0)
        {
            return true;
        }

        if (DateOnly.TryParseExact(cleaned, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
