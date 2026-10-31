using System.Text;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Import.Ing.Tests;

/// <summary>Encoding, line endings, preamble, footer and error handling.</summary>
public sealed class FileFormatTests
{
    private static readonly byte[] FakeExport = File.ReadAllBytes(IngCsv.TestDataPath("fake_ing.csv"));

    private static string FakeText => IngCsv.Windows1250.GetString(FakeExport);

    public static TheoryData<string> Variants => ["utf-8", "utf-8-bom", "utf-8-crlf", "cp1250-crlf"];

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task ReSavedVariants_ParseLikeTheOriginal(string variant)
    {
        var text = variant.EndsWith("crlf", StringComparison.Ordinal) ? FakeText.Replace("\n", "\r\n", StringComparison.Ordinal) : FakeText;
        var bytes = variant switch
        {
            "utf-8-bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            "cp1250-crlf" => IngCsv.Windows1250.GetBytes(text),
            _ => Encoding.UTF8.GetBytes(text),
        };

        var original = await Parse.AllAsync(new MemoryStream(FakeExport));
        var variantResults = await Parse.AllAsync(new MemoryStream(bytes));

        Assert.Equal(Normalize(original), Normalize(variantResults));
        Assert.Contains(variantResults.Transactions(), t => t.AccountRef == "Otwarte Konto Oszczędnościowe");
    }

    [Fact]
    public async Task Parsing_IsStreamed_AcrossTinyReads()
    {
        var original = await Parse.AllAsync(new MemoryStream(FakeExport));

        var trickled = await Parse.AllAsync(new TrickleStream(FakeExport, chunk: 7));

        Assert.Equal(Normalize(original), Normalize(trickled));
    }

    [Fact]
    public async Task LongLines_GrowTheBuffer()
    {
        var title = new string('x', 200_000);
        var row = IngCsv.CardRow().Replace("Nr karty 4111xx1234", title, StringComparison.Ordinal);

        var tx = await Parse.SingleAsync(row);

        Assert.EndsWith(title, tx.TitleRaw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Footer_IsIgnored_AndDataStopsAtTheFirstBlankLine()
    {
        var text = IngCsv.File(IngCsv.CardRow()) + "2026-10-26;2026-10-26;\"after the footer\";;;;;'1';-1,00;PLN;;;;;\"KONTO Mobi pow.26\";1,00;PLN;;;;\n";

        var results = await Parse.AllAsync(text);

        Assert.Single(results.Transactions());
        Assert.Empty(results.OfType<ParseError>());
    }

    [Fact]
    public async Task MissingHeader_FailsTheFile()
    {
        var error = await Assert.ThrowsAsync<StatementFormatException>(() => Parse.AllAsync("\"Lista transakcji\";\nhello;world\n"));

        Assert.Equal(ImportIssueCodes.HeaderNotFound, error.Code);
    }

    [Fact]
    public async Task WithoutPreamble_RowsStillParse()
    {
        var text = IngCsv.HeaderLine + "\n" + IngCsv.CardRow() + "\n";

        var results = await Parse.AllAsync(text);

        var statement = Assert.IsType<ParsedStatement>(results[0]).Statement;
        Assert.Empty(statement.Accounts);
        Assert.Null(statement.PeriodStart);
        Assert.Single(results.Transactions());
    }

    [Fact]
    public async Task TabDelimited_ParsesLikeSemicolons()
    {
        var header = string.Join('\t', IngCsvParser.Header);
        var row = string.Join('\t', "2026-10-26", "2026-10-26", "LIDL", "Płatność kartą 26.10.2026 Nr karty 4111xx1234", "'1915031/19730 '", "", "TR.KART", "'202629999127231238'", "-202,75", "PLN", "", "", "", "", "KONTO Mobi pow.26", "38787,53", "PLN");

        var tx = Assert.Single((await Parse.AllAsync($"{header}\n{row}\n")).Transactions());

        Assert.Equal(-202.75m, tx.Amount);
        Assert.Equal("202629999127231238", tx.ExternalId);
    }

    [Theory]
    [InlineData("2026-13-01;2026-10-26", ImportIssueCodes.InvalidDate)]
    [InlineData("2026-10-26;yesterday", ImportIssueCodes.InvalidDate)]
    public async Task BadRow_IsAnError_AndTheRestImports(string dates, string code)
    {
        var bad = IngCsv.CardRow().Replace("2026-10-26;2026-10-26", dates, StringComparison.Ordinal);

        var results = await Parse.AllAsync(IngCsv.File(IngCsv.CardRow(), bad, IngCsv.CardRow()));

        var error = Assert.Single(results.OfType<ParseError>()).Issue;
        Assert.Equal(code, error.Code);
        Assert.Equal(22, error.LineNumber);
        Assert.Equal(2, results.Transactions().Count());
    }

    [Theory]
    [InlineData("-12,3a", ImportIssueCodes.InvalidAmount)]
    [InlineData("", ImportIssueCodes.MissingAmount)]
    public async Task BadAmount_IsAnError(string amount, string code)
    {
        var results = await Parse.AllAsync(IngCsv.File(IngCsv.CardRow(amount: amount)));

        Assert.Equal(code, Assert.Single(results.OfType<ParseError>()).Issue.Code);
    }

    [Fact]
    public async Task ShortRow_IsAFieldCountError()
    {
        var results = await Parse.AllAsync(IngCsv.File("2026-10-26;2026-10-26;\"LIDL\";-1,00"));

        Assert.Equal(ImportIssueCodes.FieldCount, Assert.Single(results.OfType<ParseError>()).Issue.Code);
    }

    [Fact]
    public async Task UnknownDetailsCode_IsOther_AndWarnedOncePerImport()
    {
        var row = IngCsv.CardRow().Replace("Płatność kartą", "Zlecenie", StringComparison.Ordinal).Replace("\"TR.KART  \"", "\"NOWY.KOD  \"", StringComparison.Ordinal);

        var results = await Parse.AllAsync(IngCsv.File(row, row, row));

        Assert.All(results.Transactions(), t => Assert.Equal(TransactionKind.Other, t.Kind));
        var warning = Assert.Single(results.OfType<ParseWarning>()).Issue;
        Assert.Equal(ImportIssueCodes.UnknownDetailsCode, warning.Code);
        Assert.Equal("NOWY.KOD", warning.Value);
    }

    [Fact]
    public async Task RawRow_KeepsTheOriginalColumns()
    {
        var tx = await Parse.SingleAsync(IngCsv.CardRow());

        Assert.Equal(17, tx.RawRow.Fields.Count);
        Assert.Equal(21, tx.RawRow.LineNumber);
        Assert.Equal("'202629999127231238'", tx.RawRow.Fields[7]);
        Assert.Equal("TR.KART  ", tx.RawRow.Fields[6]);
    }

    [Fact]
    public void Detect_RecognisesTheExport()
    {
        var parser = new IngCsvParser(Parse.Options);

        Assert.Equal(ParserMatch.Certain, parser.Detect(FakeExport.AsSpan(0, 4096)));
        Assert.Equal(ParserMatch.Certain, parser.Detect(Encoding.UTF8.GetBytes(FakeText).AsSpan(0, 4096)));
        Assert.Equal(ParserMatch.None, parser.Detect("Date,Description,Amount\n2026-01-01,Coffee,-3.50\n"u8));
    }

    private static List<string> Normalize(List<ParseResult> results) =>
        [.. results.Select(r => r is ParsedTransaction t ? Describe(t.Transaction) : r.ToString())];

    private static string Describe(ImportedTransaction t) =>
        $"{t with { RawRow = null! }}|{t.RawRow.LineNumber}|{string.Join('|', t.RawRow.Fields)}";

    /// <summary>A non-seekable stream that returns at most <c>chunk</c> bytes per read.</summary>
    private sealed class TrickleStream(byte[] data, int chunk) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, chunk), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
