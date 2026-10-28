namespace ExpenseInsights.Core.Import;

/// <summary>
/// A row error or import warning. Stored as a code plus parameters so the UI can word it
/// (in Polish) itself; never holds names, addresses or account numbers.
/// </summary>
public sealed record ImportIssue(
    string Code,
    int? LineNumber = null,
    string? Account = null,
    string? Value = null,
    decimal? Expected = null,
    decimal? Actual = null,
    DateOnly? From = null,
    DateOnly? To = null);

public static class ImportIssueCodes
{
    // File-level (StatementFormatException).
    public const string UnsupportedFormat = "unsupportedFormat";
    public const string HeaderNotFound = "headerNotFound";
    public const string HeaderMismatch = "headerMismatch";

    // Row errors.
    public const string FieldCount = "fieldCount";
    public const string InvalidDate = "invalidDate";
    public const string InvalidAmount = "invalidAmount";
    public const string MissingAmount = "missingAmount";
    public const string MissingCurrency = "missingCurrency";
    public const string MissingTransactionId = "missingTransactionId";
    public const string UnknownAccount = "unknownAccount";

    // Warnings.
    public const string UnknownDetailsCode = "unknownDetailsCode";
    public const string BalanceMismatch = "balanceMismatch";
    public const string TotalsCountMismatch = "totalsCountMismatch";
    public const string TotalsSumMismatch = "totalsSumMismatch";
    public const string PeriodGap = "periodGap";
}
