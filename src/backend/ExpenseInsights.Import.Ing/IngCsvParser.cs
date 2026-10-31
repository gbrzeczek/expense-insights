using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Import.Ing;

/// <summary>
/// Parses the ING Bank Śląski "Lista transakcji" CSV export: Windows-1250 (UTF-8 tolerated),
/// <c>;</c>-delimited, a preamble before the header, data rows newest first, then a footer.
/// </summary>
public sealed class IngCsvParser(IngParserOptions options) : IStatementParser
{
    public const string Id = "ing-pl";

    /// <summary>The 17 named columns. <c>Waluta</c> appears four times, so columns map by position.</summary>
    internal static readonly string[] Header =
    [
        "Data transakcji", "Data księgowania", "Dane kontrahenta", "Tytuł", "Nr rachunku", "Nazwa banku",
        "Szczegóły", "Nr transakcji", "Kwota transakcji (waluta rachunku)", "Waluta",
        "Kwota blokady/zwolnienie blokady", "Waluta", "Kwota płatności w walucie", "Waluta", "Konto",
        "Saldo po transakcji", "Waluta",
    ];

    private const int TransactionDate = 0;
    private const int BookingDate = 1;
    private const int Counterparty = 2;
    private const int Title = 3;
    private const int CounterpartyAccount = 4;
    private const int CounterpartyBank = 5;
    private const int Details = 6;
    private const int ExternalId = 7;
    private const int Amount = 8;
    private const int Currency = 9;
    private const int HoldAmount = 10;
    private const int HoldCurrency = 11;
    private const int OriginalAmount = 12;
    private const int OriginalCurrency = 13;
    private const int Account = 14;
    private const int Balance = 15;

    public string BankId => Id;

    public ParserMatch Detect(ReadOnlySpan<byte> head)
    {
        var text = LineReader.DecodeHead(head);
        if (text.Contains(Header[0], StringComparison.Ordinal)
            && text.Contains(Header[1], StringComparison.Ordinal)
            && text.Contains(Header[7], StringComparison.Ordinal))
        {
            return ParserMatch.Certain;
        }

        return text.Contains("ING Bank Śląski", StringComparison.OrdinalIgnoreCase) ? ParserMatch.Likely : ParserMatch.None;
    }

    public async IAsyncEnumerable<ParseResult> ParseAsync(Stream csv, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new LineReader(csv);
        var preamble = new IngPreamble();
        char delimiter;

        // Find the header by its signature, not by line number.
        while (true)
        {
            var line = await reader.ReadLineAsync(ct)
                ?? throw new StatementFormatException(ImportIssueCodes.HeaderNotFound, "The ING header line was not found.");
            delimiter = line.Contains(';', StringComparison.Ordinal) || !line.Contains('\t', StringComparison.Ordinal) ? ';' : '\t';
            var fields = CsvLine.Split(line, delimiter);
            if (IngFields.Clean(fields[0]) == Header[0])
            {
                ValidateHeader(fields);
                break;
            }

            preamble.Read(fields);
        }

        yield return new ParsedStatement(preamble.ToStatement());

        var unknownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contentKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            // The data ends at the first blank line; the footer after it is ignored.
            if (line.Trim().Trim(delimiter).Trim().Length == 0)
            {
                yield break;
            }

            var result = ParseRow(CsvLine.Split(line, delimiter), reader.LineNumber, contentKeys, out var unknownCode);
            if (unknownCode is not null && unknownCodes.Add(unknownCode))
            {
                yield return new ParseWarning(new ImportIssue(
                    ImportIssueCodes.UnknownDetailsCode, LineNumber: reader.LineNumber, Value: unknownCode));
            }

            yield return result;
        }
    }

    private static void ValidateHeader(List<string> fields)
    {
        if (fields.Count < Header.Length || !Header.Select((name, i) => IngFields.Clean(fields[i]) == name).All(ok => ok))
        {
            throw new StatementFormatException(ImportIssueCodes.HeaderMismatch, "The ING header has unexpected columns.");
        }
    }

    private ParseResult ParseRow(List<string> fields, int line, Dictionary<string, int> contentKeys, out string? unknownCode)
    {
        unknownCode = null;
        if (fields.Count < Header.Length)
        {
            return Error(ImportIssueCodes.FieldCount, line, fields.Count.ToString(CultureInfo.InvariantCulture));
        }

        if (!IngFields.TryParseDate(fields[TransactionDate], out var transactionDate) || transactionDate is null)
        {
            return Error(ImportIssueCodes.InvalidDate, line, IngFields.Clean(fields[TransactionDate]));
        }

        if (!IngFields.TryParseDate(fields[BookingDate], out var bookingDate))
        {
            return Error(ImportIssueCodes.InvalidDate, line, IngFields.Clean(fields[BookingDate]));
        }

        int[] amountColumns = [Amount, HoldAmount, OriginalAmount, Balance];
        var amounts = new decimal?[amountColumns.Length];
        for (var i = 0; i < amountColumns.Length; i++)
        {
            if (!IngFields.TryParseAmount(fields[amountColumns[i]], out amounts[i]))
            {
                return Error(ImportIssueCodes.InvalidAmount, line, IngFields.Clean(fields[amountColumns[i]]));
            }
        }

        var (amount, holdAmount, originalAmount, balance) = (amounts[0], amounts[1], amounts[2], amounts[3]);
        var externalId = IngFields.CleanOrNull(fields[ExternalId]);

        // A hold (blokada) has no booking date and no id, and its amount is in the hold column.
        var pending = bookingDate is null && externalId is null && amount is null && holdAmount is not null;
        var (value, currency) = pending
            ? (holdAmount, IngFields.CleanOrNull(fields[HoldCurrency]))
            : (amount, IngFields.CleanOrNull(fields[Currency]));
        if (value is null)
        {
            return Error(ImportIssueCodes.MissingAmount, line);
        }

        if (currency is null)
        {
            return Error(ImportIssueCodes.MissingCurrency, line);
        }

        // Some booked rows come without a number (seen in a real export, otherwise ordinary rows).
        // They are real movements in the balance chain and the totals, so they are kept, with a key
        // made from their content for dedup across overlapping exports.
        if (!pending && externalId is null)
        {
            externalId = ContentKey(fields, contentKeys);
        }

        var counterpartyName = IngFields.CollapseSpaces(fields[Counterparty]);
        var title = IngFields.CollapseSpaces(fields[Title]);
        var code = IngFields.Clean(fields[Details]);
        var kind = IngTransactionKinds.Detect(code, title, out var unknown);
        if (unknown)
        {
            unknownCode = code;
        }

        return new ParsedTransaction(new ImportedTransaction
        {
            BankId = Id,
            AccountRef = IngFields.CollapseSpaces(fields[Account]),
            ExternalId = externalId,
            Status = pending ? TransactionStatus.Pending : TransactionStatus.Booked,
            TransactionDate = transactionDate.Value,
            BookingDate = bookingDate,
            PurchaseDate = kind == TransactionKind.Card ? IngTransactionKinds.PurchaseDate(title) : null,
            Amount = value.Value,
            Currency = currency,
            OriginalAmount = originalAmount,
            OriginalCurrency = originalAmount is null ? null : IngFields.CleanOrNull(fields[OriginalCurrency]),
            Kind = kind,
            CounterpartyRaw = fields[Counterparty],
            TitleRaw = fields[Title],
            CounterpartyName = counterpartyName,
            MerchantHint = MerchantHints.Extract(counterpartyName, title, options.PaymentProcessors),
            CounterpartyAccount = IngFields.CleanOrNull(fields[CounterpartyAccount]),
            CounterpartyBank = IngFields.CleanOrNull(fields[CounterpartyBank]),
            BalanceAfter = balance,
            RawRow = new RawRow(line, TrimTrailingEmpty(fields)),
        });
    }

    /// <summary>The prefix of <see cref="ContentKey"/>s; ING's own numbers are digits only.</summary>
    internal const string ContentKeyPrefix = "ing-row:";

    /// <summary>
    /// A stable id for a booked row without a number: a hash of the columns that don't change between
    /// exports (the balance after it makes it unique in practice), plus <c>#2</c>, <c>#3</c> … for
    /// identical rows in one file.
    /// </summary>
    private static string ContentKey(List<string> fields, Dictionary<string, int> seen)
    {
        int[] columns = [TransactionDate, BookingDate, Counterparty, Title, CounterpartyAccount, Details, Amount, Currency, Account, Balance];
        var content = string.Join('\u001f', columns.Select(c => IngFields.CollapseSpaces(IngFields.Clean(fields[c]))));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)).AsSpan(0, 16));
        var key = ContentKeyPrefix + hash;
        var count = seen[key] = seen.GetValueOrDefault(key) + 1;
        return count == 1 ? key : $"{key}#{count.ToString(CultureInfo.InvariantCulture)}";
    }

    private static List<string> TrimTrailingEmpty(List<string> fields)
    {
        var count = fields.Count;
        while (count > Header.Length && fields[count - 1].Length == 0)
        {
            count--;
        }

        return fields.GetRange(0, count);
    }

    private static ParseError Error(string code, int line, string? value = null) =>
        new(new ImportIssue(code, LineNumber: line, Value: value));
}
