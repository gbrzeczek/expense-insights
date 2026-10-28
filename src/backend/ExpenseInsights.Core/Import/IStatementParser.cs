namespace ExpenseInsights.Core.Import;

/// <summary>
/// A bank-specific statement parser. Everything downstream works on <see cref="ParseResult"/>
/// and never knows which bank a row came from.
/// </summary>
public interface IStatementParser
{
    /// <summary>Stable bank identifier, e.g. <c>ing-pl</c>.</summary>
    string BankId { get; }

    /// <summary>Cheap sniffing on the first bytes of the file.</summary>
    ParserMatch Detect(ReadOnlySpan<byte> head);

    /// <summary>
    /// Streams the file. Yields one <see cref="ParsedStatement"/> before any rows, then a
    /// <see cref="ParsedTransaction"/> or a <see cref="ParseError"/> per data row, plus any
    /// <see cref="ParseWarning"/>s. Throws <see cref="StatementFormatException"/> when the file as a
    /// whole can't be read.
    /// </summary>
    IAsyncEnumerable<ParseResult> ParseAsync(Stream csv, CancellationToken ct);
}

public enum ParserMatch
{
    None,
    Likely,
    Certain,
}
