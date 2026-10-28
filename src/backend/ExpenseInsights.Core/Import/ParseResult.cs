namespace ExpenseInsights.Core.Import;

public abstract record ParseResult;

/// <summary>Statement-level metadata (from the file's preamble). Always the first result.</summary>
public sealed record ParsedStatement(StatementInfo Statement) : ParseResult;

public sealed record ParsedTransaction(ImportedTransaction Transaction) : ParseResult;

/// <summary>A row that couldn't be parsed. One bad row never fails the whole import.</summary>
public sealed record ParseError(ImportIssue Issue) : ParseResult;

/// <summary>Something worth telling the user that doesn't make a row invalid.</summary>
public sealed record ParseWarning(ImportIssue Issue) : ParseResult;

public sealed class StatementFormatException(string code, string message) : Exception(message)
{
    /// <summary>One of <see cref="ImportIssueCodes"/>.</summary>
    public string Code { get; } = code;
}
