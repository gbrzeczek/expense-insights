using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Import.Ing.Tests;

/// <summary>One test per quirk in plan 02's <i>Known format</i> table.</summary>
public sealed class QuirkTests
{
    [Fact]
    public async Task DuplicateHeaderNames_ColumnsMapByPosition()
    {
        var tx = await Parse.SingleAsync(IngCsv.CardRow(amount: "-21,37", fxAmount: "-5,00", fxCurrency: "EUR", balance: "100,00"));

        Assert.Equal(-21.37m, tx.Amount);
        Assert.Equal("PLN", tx.Currency);
        Assert.Equal(-5.00m, tx.OriginalAmount);
        Assert.Equal("EUR", tx.OriginalCurrency);
        Assert.Equal(100.00m, tx.BalanceAfter);
    }

    [Fact]
    public async Task HeaderSignatureMismatch_FailsTheFile()
    {
        var swapped = IngCsv.HeaderLine.Replace("\"Konto\";\"Saldo po transakcji\"", "\"Saldo po transakcji\";\"Konto\"", StringComparison.Ordinal);
        var text = IngCsv.File(IngCsv.CardRow()).Replace(IngCsv.HeaderLine, swapped, StringComparison.Ordinal);

        var error = await Assert.ThrowsAsync<StatementFormatException>(() => Parse.AllAsync(text));

        Assert.Equal(ImportIssueCodes.HeaderMismatch, error.Code);
    }

    [Fact]
    public async Task ApostropheWrappedValues_AreStrippedAndTrimmed()
    {
        var tx = await Parse.SingleAsync(IngCsv.CardRow(id: "'202630164005767692'"));

        Assert.Equal("202630164005767692", tx.ExternalId);
        Assert.Equal("1915031/19730", tx.CounterpartyAccount);
    }

    [Fact]
    public async Task Spaces_AreCollapsedInTheName_AndKeptRaw()
    {
        var tx = await Parse.SingleAsync(IngCsv.CardRow(counterparty: " Bershka.com               Arteixo "));

        Assert.Equal("Bershka.com Arteixo", tx.CounterpartyName);
        Assert.Equal(" Bershka.com               Arteixo ", tx.CounterpartyRaw);
        Assert.Equal(" Bershka.com               Arteixo ", tx.RawRow.Fields[2]);
    }

    [Theory]
    [InlineData("-298,80", -298.80)]
    [InlineData("22055,82", 22055.82)]
    [InlineData("-0,01", -0.01)]
    [InlineData("10400", 10400)]
    public async Task DecimalComma_ParsesExactly(string text, double expected)
    {
        var tx = await Parse.SingleAsync(IngCsv.CardRow(amount: text));

        Assert.Equal((decimal)expected, tx.Amount);
    }

    [Fact]
    public async Task PendingCardPayment_IsPendingWithHoldAmount()
    {
        const string row = "2026-10-28;;\" ZABKA Z5017 K.1  KRAKOW  POL \";\" Płatność kartą  28.10.2026 Nr karty 4111xx1234\";;;;;;;-10,33;PLN;;;\"KONTO Mobi pow.26\";38211,34;PLN;;;;";

        var tx = await Parse.SingleAsync(row);

        Assert.Equal(TransactionStatus.Pending, tx.Status);
        Assert.Null(tx.ExternalId);
        Assert.Null(tx.BookingDate);
        Assert.Equal(-10.33m, tx.Amount);
        Assert.Equal("PLN", tx.Currency);
        Assert.Equal(TransactionKind.Card, tx.Kind);
        // The title of a hold has a double space; the date is still found.
        Assert.Equal(new DateOnly(2026, 10, 28), tx.PurchaseDate);
    }

    [Fact]
    public async Task PurchaseDate_IsParsedFromTheTitle()
    {
        var row = IngCsv.CardRow(date: "2026-10-25", booked: "2026-10-27");

        var tx = await Parse.SingleAsync(row);

        Assert.Equal(new DateOnly(2026, 10, 25), tx.PurchaseDate);
        Assert.Equal(new DateOnly(2026, 10, 27), tx.BookingDate);
        Assert.Equal(TransactionStatus.Booked, tx.Status);
    }

    [Fact]
    public async Task BlikThroughProcessor_IsBlikOnlineWithMerchantHint()
    {
        const string row = "2026-09-17;2026-09-17;\" Cashbill eComm Sobieskiego 2 Katowice \";\" Płatność BLIK 17.09.2026 Nr transakcji 36781865610 doci.pl\";;;\"TR.BLIK  \";'202626064197831034';-7,06;PLN;;;;;\"KONTO Mobi pow.26\";36407,22;PLN;;;;";

        var tx = await Parse.SingleAsync(row);

        Assert.Equal(TransactionKind.BlikOnline, tx.Kind);
        Assert.Equal("doci.pl", tx.MerchantHint);
        Assert.Null(tx.PurchaseDate);
    }

    [Theory]
    [InlineData(" PayU S.A. Grunwaldzka 186 Poznan ", "Nr transakcji 1 https://www.Allegro.pl/zamowienie", "allegro.pl")]
    [InlineData(" Zielone Sklepy ul. Polna Wieliczka ", "Nr transakcji 1 https://zielone-sklepy.pl", null)]
    [InlineData(" PayU S.A. Grunwaldzka 186 Poznan ", "Nr transakcji 1 zamowienie", null)]
    [InlineData(" PayU S.A. Grunwaldzka 186 Poznan ", "Nr transakcji 1 v1.2", null)]
    public async Task MerchantHint_OnlyForProcessors_AndOnlyForDomains(string counterparty, string titleEnd, string? expected)
    {
        var row = $"2026-09-17;2026-09-17;\"{counterparty}\";\" Płatność BLIK 17.09.2026 {titleEnd}\";;;\"TR.BLIK  \";'202626064197831034';-7,06;PLN;;;;;\"KONTO Mobi pow.26\";1,00;PLN;;;;";

        var tx = await Parse.SingleAsync(row);

        Assert.Equal(expected, tx.MerchantHint);
    }

    [Fact]
    public async Task MerchantHint_ProcessorListComesFromOptions()
    {
        const string row = "2026-09-17;2026-09-17;\" Cashbill eComm Sobieskiego 2 Katowice \";\" Płatność BLIK 17.09.2026 Nr transakcji 1 doci.pl\";;;\"TR.BLIK  \";'1';-7,06;PLN;;;;;\"KONTO Mobi pow.26\";1,00;PLN;;;;";
        var parser = new IngCsvParser(new IngParserOptions());

        var results = new List<ParseResult>();
        await foreach (var result in parser.ParseAsync(IngCsv.Stream(IngCsv.File(row)), TestContext.Current.CancellationToken))
        {
            results.Add(result);
        }

        Assert.Null(Assert.Single(results.Transactions()).MerchantHint);
    }

    [Fact]
    public async Task IncomingBlikP2P_IsBlikP2P()
    {
        const string row = "2026-10-21;2026-10-21;\" Katarzyna Zielińska \";\" pizza\";'02998935449268315893571266 ';\"ALIOR Centrala\";\"P.BLIK  \";'202629464565783275';98,59;PLN;;;;;\"KONTO Mobi pow.26\";39399,38;PLN;;;;";

        var tx = await Parse.SingleAsync(row);

        Assert.Equal(TransactionKind.BlikP2P, tx.Kind);
        Assert.Equal(98.59m, tx.Amount);
        Assert.Equal("Katarzyna Zielińska", tx.CounterpartyName);
        Assert.Equal("02998935449268315893571266", tx.CounterpartyAccount);
        Assert.Equal("ALIOR Centrala", tx.CounterpartyBank);
    }

    [Fact]
    public async Task PhoneTransfer_TitleInEnglish_IsPhoneTransfer()
    {
        const string row = "2026-10-26;2026-10-26;\" Michał Wójcik \";\" Telephone transfer +48xxxxxx379 Phone transfer To Michał Wójcik From JAN KOWALSKI\";'89465398402891416128463985 ';\"PKO BP S.A.\";\"PRZELEW  \";'202629997155082939';-120,00;PLN;;;;;\"KONTO Mobi pow.26\";38667,53;PLN;;;;";

        var tx = await Parse.SingleAsync(row);

        Assert.Equal(TransactionKind.PhoneTransfer, tx.Kind);
    }

    [Fact]
    public async Task TruncatedCountryAndCity_AreLeftForMerchantNormalization()
    {
        var tx = await Parse.SingleAsync(IngCsv.CardRow(counterparty: " JMP S.A. BIEDRONKA 6537  KRAKOW  PO "));

        Assert.Equal("JMP S.A. BIEDRONKA 6537 KRAKOW PO", tx.CounterpartyName);
    }

    [Fact]
    public async Task ForeignCurrency_FillsOriginalAmountAndCurrency()
    {
        var tx = await Parse.SingleAsync(IngCsv.CardRow(counterparty: " LIDL DIENSTLEISTUNG  Berlin  DEU ", amount: "-52,71", fxAmount: "-12,34", fxCurrency: "EUR"));

        Assert.Equal(-52.71m, tx.Amount);
        Assert.Equal("PLN", tx.Currency);
        Assert.Equal(-12.34m, tx.OriginalAmount);
        Assert.Equal("EUR", tx.OriginalCurrency);
    }

    [Fact]
    public async Task BookingDateBeforeTransactionDate_IsAllowed()
    {
        const string row = "2026-10-01;2026-09-30;\" ING BANK ŚLĄSKI S.A. UL.SOKOLSKA 34 40-086 KATOWICE \";\" Kapitalizacja odsetek\";;;\"ODSETKI  \";'202627397047142443';50,95;PLN;;;;;\"Otwarte Konto Oszczędnościowe\";50199,50;PLN;;;;";

        var results = await Parse.AllAsync(IngCsv.File(row));

        var tx = Assert.Single(results.Transactions());
        Assert.Empty(results.OfType<ParseError>());
        Assert.Empty(results.OfType<ParseWarning>());
        Assert.Equal(new DateOnly(2026, 10, 1), tx.TransactionDate);
        Assert.Equal(new DateOnly(2026, 9, 30), tx.BookingDate);
        Assert.Equal(TransactionKind.Other, tx.Kind);
    }

    [Fact]
    public async Task OwnTransfer_BothLegsAreKept()
    {
        string[] rows =
        [
            "2026-10-11;2026-10-11;\" JAN KOWALSKI \";\" Przelew własny - oszczędności\";'12104332181960013389083863 ';\"ING Bank Śląski S.A.\";\"PRZELEW  \";'202628497022541876';2000,00;PLN;;;;;\"Otwarte Konto Oszczędnościowe\";52189,82;PLN;;;;",
            "2026-10-11;2026-10-11;\" JAN KOWALSKI \";\" Przelew własny - oszczędności\";'74794026542351161559407816 ';\"ING Bank Śląski S.A.\";\"PRZELEW  \";'202628497108652175';-2000,00;PLN;;;;;\"KONTO Mobi pow.26\";40586,18;PLN;;;;",
        ];

        var transactions = (await Parse.AllAsync(IngCsv.File(rows))).Transactions().ToList();

        Assert.Equal(2, transactions.Count);
        Assert.Equal(["Otwarte Konto Oszczędnościowe", "KONTO Mobi pow.26"], transactions.Select(t => t.AccountRef));
        Assert.Equal([2000m, -2000m], transactions.Select(t => t.Amount));
        Assert.Equal(["12104332181960013389083863", "74794026542351161559407816"], transactions.Select(t => t.CounterpartyAccount));
    }

    [Theory]
    [InlineData("ST.ZLEC", " SPÓŁDZIELNIA MIESZKANIOWA PODGÓRZE ", " CZYNSZ 10/2026 LOK. 15", "-742,18")]
    [InlineData("EXPRESS", " Anna Nowak ", " za bilety", "-150,00")]
    [InlineData("PZ", " PZU SA AL. JANA PAWŁA II 24 WARSZAWA ", " Składka polisa 09/2026", "-186,00")]
    public async Task StandingOrder_ExpressTransfer_AndTransferOrder_AreTransfers(string code, string counterparty, string title, string amount)
    {
        var row = $"2026-10-01;2026-10-01;\"{counterparty}\";\"{title}\";'87184959310341316475255341 ';\"PKO BP S.A.\";\"{code}  \";'202627497213806556';{amount};PLN;;;;;\"KONTO Mobi pow.26\";32955,86;PLN;;;;";

        var results = await Parse.AllAsync(IngCsv.File(row));

        Assert.Empty(results.OfType<ParseWarning>());
        var tx = Assert.Single(results.Transactions());
        Assert.Equal(TransactionKind.Transfer, tx.Kind);
        Assert.Null(tx.PurchaseDate);
        Assert.Equal("87184959310341316475255341", tx.CounterpartyAccount);
    }

    [Fact]
    public async Task BookedWithoutNumber_IsKept_WithAKeyFromItsContent()
    {
        // Corrections that cancel out: booked, with a balance, but no Nr transakcji and no code.
        string[] rows =
        [
            Correction(" Storno korekty księgowania", "58,88", "33607,11"),
            Correction(" Korekta księgowania", "-58,88", "33548,23"),
        ];

        var results = await Parse.AllAsync(IngCsv.File(rows));

        Assert.Empty(results.OfType<ParseError>());
        var transactions = results.Transactions().ToList();
        Assert.Equal([58.88m, -58.88m], transactions.Select(t => t.Amount));
        Assert.All(transactions, t =>
        {
            Assert.Equal(TransactionStatus.Booked, t.Status);
            Assert.Equal(TransactionKind.Other, t.Kind);
            Assert.StartsWith(IngCsvParser.ContentKeyPrefix, t.ExternalId, StringComparison.Ordinal);
            Assert.True(t.ExternalId!.Length <= 64);
        });
        Assert.NotEqual(transactions[0].ExternalId, transactions[1].ExternalId);
        Assert.Equal(CheckResult.Passed, StatementChecks.CheckBalances(transactions).Result);
    }

    [Fact]
    public async Task BookedWithoutNumber_KeepsItsKey_InAnotherExport()
    {
        var row = Correction(" Korekta księgowania", "-58,88", "33548,23");

        var alone = await Parse.SingleAsync(row);
        var later = (await Parse.AllAsync(IngCsv.File(IngCsv.CardRow(), IngCsv.CardRow(id: "'202629999127231239'"), row))).Transactions().Last();

        Assert.NotEqual(alone.RawRow.LineNumber, later.RawRow.LineNumber);
        Assert.Equal(alone.ExternalId, later.ExternalId);
    }

    [Fact]
    public async Task BookedWithoutNumber_IdenticalRowsInOneFile_GetDistinctKeys()
    {
        var row = Correction(" Korekta księgowania", "-58,88", "33548,23");

        var transactions = (await Parse.AllAsync(IngCsv.File(row, row))).Transactions().ToList();

        Assert.Equal(transactions[0].ExternalId + "#2", transactions[1].ExternalId);
    }

    private static string Correction(string title, string amount, string balance) =>
        $"2026-08-24;2026-08-24;\" ING BANK ŚLĄSKI S.A. UL.SOKOLSKA 34 40-086 KATOWICE \";\"{title}\";;;;;{amount};PLN;;;;;\"KONTO Mobi pow.26\";{balance};PLN;;;;";

    [Fact]
    public async Task TransactionId_IsKeptOpaque()
    {
        var tx = await Parse.SingleAsync(IngCsv.CardRow(id: "'000000000000000042'"));

        Assert.Equal("000000000000000042", tx.ExternalId);
    }
}
