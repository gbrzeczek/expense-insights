using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Testing;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Tests.Import;

public sealed class StatementImporterTests(PostgresFixture postgres) : ImportTestBase(postgres)
{
    [Fact]
    public async Task FakeExport_ImportsWithoutErrors_AndChecksPass()
    {
        var user = await NewUserAsync();

        var (batch, alreadyImported) = await ImportAsync(user, "fake_ing.csv");

        Assert.False(alreadyImported);
        Assert.Equal("ing-pl", batch.BankId);
        Assert.Equal(367, batch.RowCount);
        Assert.Equal(367, batch.NewCount);
        Assert.Equal(0, batch.ErrorCount);
        Assert.Empty(batch.Errors);
        Assert.Empty(batch.Warnings);
        Assert.Equal(CheckResult.Passed, batch.TotalsCheck);
        Assert.Equal(CheckResult.Passed, batch.BalanceCheck);
        Assert.Equal(new DateOnly(2026, 5, 1), batch.PeriodStart);
        Assert.Equal(new DateOnly(2026, 10, 28), batch.PeriodEnd);
        Assert.Equal("0123456789_281026", batch.DocumentId);

        await using var db = CreateDbContext(user);
        Assert.Equal(367, await db.Transactions.CountAsync(CancellationToken));
        Assert.Equal(4, await db.Transactions.CountAsync(t => t.Status == TransactionStatus.Pending, CancellationToken));
        var stored = await db.ImportBatches.SingleAsync(CancellationToken);
        Assert.Equal(CheckResult.Passed, stored.TotalsCheck);
        Assert.Equal(batch.FileHash, stored.FileHash);
    }

    [Fact]
    public async Task FakeExport_CreatesAccountsFromThePreamble_WithoutFullNumbers()
    {
        var user = await NewUserAsync();

        await ImportAsync(user, "fake_ing.csv");

        await using var db = CreateDbContext(user);
        var accounts = await db.Accounts.OrderBy(a => a.Name).ToListAsync(CancellationToken);
        Assert.Equal(["KONTO Mobi pow.26", "Otwarte Konto Oszczędnościowe"], accounts.Select(a => a.Name));
        Assert.Equal(["•••• 3863", "•••• 7816"], accounts.Select(a => a.NumberMasked));
        Assert.Equal(AccountNumber.Hash("12104332181960013389083863"), accounts[0].NumberHash);
        Assert.All(accounts, a => Assert.Equal("PLN", a.Currency));
        Assert.Equal(1, await db.Transactions.CountAsync(t => t.AccountId == accounts[1].Id && t.Amount == 2000m && t.TransactionDate == new DateOnly(2026, 10, 11), CancellationToken));
    }

    [Fact]
    public async Task FakeExport_KeepsTheRawRowAsJson()
    {
        var user = await NewUserAsync();

        await ImportAsync(user, "fake_ing.csv");

        await using var db = CreateDbContext(user);
        var tx = await db.Transactions.SingleAsync(t => t.ExternalId == "202630164140439719", CancellationToken);
        Assert.Equal(22, tx.RawRow.LineNumber);
        Assert.Equal(17, tx.RawRow.Fields.Count);
        Assert.Equal("'202630164140439719'", tx.RawRow.Fields[7]);
        Assert.Equal("pyszne.pl", tx.MerchantHint);
        Assert.Equal(TransactionKind.BlikOnline, tx.Kind);

        var json = await db.Database
            .SqlQuery<string>($"SELECT \"RawRow\"::text AS \"Value\" FROM \"Transactions\" WHERE \"Id\" = {tx.Id}")
            .SingleAsync(CancellationToken);
        Assert.Contains("PayU S.A. Grunwaldzka 186 Poznan", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnTransfers_AreMarked_AndBothLegsLinked()
    {
        var user = await NewUserAsync();

        await ImportAsync(user, "fake_ing.csv");

        await using var db = CreateDbContext(user);
        var own = await db.Transactions.Where(t => t.Kind == TransactionKind.OwnTransfer).ToListAsync(CancellationToken);
        Assert.Equal(12, own.Count);
        Assert.All(own, t =>
        {
            var pair = Assert.Single(own, o => o.Id == t.OwnTransferPairId);
            Assert.Equal(t.Id, pair.OwnTransferPairId);
            Assert.Equal(-t.Amount, pair.Amount);
            Assert.NotEqual(t.AccountId, pair.AccountId);
        });
    }

    [Fact]
    public async Task SameFileTwice_IsANoOp()
    {
        var user = await NewUserAsync();
        var first = await ImportAsync(user, "fake_ing.csv");

        var second = await ImportAsync(user, "fake_ing.csv");

        Assert.True(second.AlreadyImported);
        Assert.Equal(first.Batch.Id, second.Batch.Id);
        await using var db = CreateDbContext(user);
        Assert.Equal(367, await db.Transactions.CountAsync(CancellationToken));
        Assert.Equal(1, await db.ImportBatches.CountAsync(CancellationToken));
    }

    [Fact]
    public async Task SameRowsInADifferentFile_AreDuplicates()
    {
        var user = await NewUserAsync();
        var file = TestData.Read("fake_ing.csv");
        await ImportAsync(user, file);

        // Re-saved by a phone: UTF-8 and CRLF, so a different hash but the same rows.
        var resaved = System.Text.Encoding.UTF8.GetBytes(Decode(file).Replace("\n", "\r\n", StringComparison.Ordinal));
        var (batch, alreadyImported) = await ImportAsync(user, resaved);

        Assert.False(alreadyImported);
        Assert.Equal(0, batch.NewCount);
        Assert.Equal(0, batch.UpdatedCount);
        Assert.Equal(367, batch.DuplicateCount);
        await using var db = CreateDbContext(user);
        Assert.Equal(367, await db.Transactions.CountAsync(CancellationToken));
    }

    [Fact]
    public async Task OverlappingExports_PromotePendingRows_WithoutDuplicates()
    {
        var user = await NewUserAsync();
        var a = await ImportAsync(user, "fake_ing_overlap_a.csv");
        await using var before = CreateDbContext(user);
        var pendingInA = await before.Transactions.AsNoTracking().Where(t => t.Status == TransactionStatus.Pending).ToListAsync(CancellationToken);

        var b = await ImportAsync(user, "fake_ing_overlap_b.csv");

        Assert.Equal(6, pendingInA.Count);
        Assert.Equal(0, a.Batch.ErrorCount);
        Assert.Equal(0, b.Batch.ErrorCount);
        Assert.Equal(6, b.Batch.UpdatedCount);
        Assert.Empty(b.Batch.Warnings);
        await using var db = CreateDbContext(user);
        var all = await db.Transactions.ToListAsync(CancellationToken);
        Assert.Equal(all.Count, all.Where(t => t.ExternalId != null).Select(t => (t.AccountId, t.ExternalId)).Distinct().Count() + all.Count(t => t.ExternalId == null));
        foreach (var hold in pendingInA)
        {
            var now = Assert.Single(all, t => t.Id == hold.Id);
            Assert.Equal(TransactionStatus.Booked, now.Status);
            Assert.NotNull(now.ExternalId);
            Assert.NotNull(now.BookingDate);
            Assert.Equal(hold.Amount, now.Amount);
        }

        // Every row of both files once: the union of distinct transactions.
        Assert.Equal(a.Batch.NewCount + b.Batch.NewCount, all.Count);
        Assert.Equal(b.Batch.RowCount, b.Batch.NewCount + b.Batch.UpdatedCount + b.Batch.DuplicateCount);
    }

    [Fact]
    public async Task OverlappingExports_InReverseOrder_DoNotDuplicateHolds()
    {
        var user = await NewUserAsync();
        await ImportAsync(user, "fake_ing_overlap_b.csv");

        var a = await ImportAsync(user, "fake_ing_overlap_a.csv");

        await using var db = CreateDbContext(user);
        Assert.Equal(0, await db.Transactions.CountAsync(t => t.ImportBatchId == a.Batch.Id && t.Status == TransactionStatus.Pending, CancellationToken));
        var pending = await db.Transactions.CountAsync(t => t.Status == TransactionStatus.Pending, CancellationToken);
        Assert.Equal(4, pending);
    }

    [Fact]
    public async Task StalePendingRow_MissingFromANewerExport_IsExpired_NotDeleted()
    {
        var user = await NewUserAsync();
        const string hold = "2026-10-01;;\" KIOSK RUCH  KRAKOW  POL \";\" Płatność kartą  01.10.2026 Nr karty 4111xx1234\";;;;;;;-9,99;PLN;;;\"KONTO Mobi pow.26\";100,00;PLN;;;;";
        const string recent = "2026-10-20;;\" KIOSK RUCH  KRAKOW  POL \";\" Płatność kartą  20.10.2026 Nr karty 4111xx1234\";;;;;;;-5,00;PLN;;;\"KONTO Mobi pow.26\";95,00;PLN;;;;";
        await ImportAsync(user, Encode(FakeIngExport.File("2026-09-01", "2026-10-01", hold)));
        await ImportAsync(user, Encode(FakeIngExport.File("2026-10-02", "2026-10-20", recent)));

        var (batch, _) = await ImportAsync(user, Encode(FakeIngExport.File("2026-09-15", "2026-10-28", FakeIngExport.CardRow("2026-10-27", "-1,00", "'1'"))));

        Assert.Equal(1, batch.ExpiredCount);
        await using var db = CreateDbContext(user);
        var statuses = await db.Transactions.OrderBy(t => t.TransactionDate).Select(t => t.Status).ToListAsync(CancellationToken);
        // The 20 Oct hold is only 8 days old: it may still be booked.
        Assert.Equal([TransactionStatus.Expired, TransactionStatus.Pending, TransactionStatus.Booked], statuses);
    }

    [Fact]
    public async Task BookedRowsWithoutNumber_AreImported_AndDedupedInALaterExport()
    {
        var user = await NewUserAsync();
        const string correction = "2026-10-05;2026-10-05;\" ING BANK ŚLĄSKI S.A. UL.SOKOLSKA 34 40-086 KATOWICE \";\" Korekta księgowania\";;;;;-58,88;PLN;;;;;\"KONTO Mobi pow.26\";941,12;PLN;;;;";
        const string storno = "2026-10-05;2026-10-05;\" ING BANK ŚLĄSKI S.A. UL.SOKOLSKA 34 40-086 KATOWICE \";\" Storno korekty księgowania\";;;;;58,88;PLN;;;;;\"KONTO Mobi pow.26\";1000,00;PLN;;;;";
        var first = await ImportAsync(user, Encode(FakeIngExport.File("2026-10-01", "2026-10-10", storno, correction)));

        // A later export: another row on top, so the same rows sit on other lines.
        var later = await ImportAsync(user, Encode(FakeIngExport.File(
            "2026-10-01", "2026-10-20", FakeIngExport.CardRow("2026-10-15", "-1,00", "'1'", balance: "999,00"), storno, correction)));

        Assert.Equal((2, 0, CheckResult.Passed, CheckResult.Passed), (first.Batch.NewCount, first.Batch.ErrorCount, first.Batch.TotalsCheck, first.Batch.BalanceCheck));
        Assert.Equal((1, 2), (later.Batch.NewCount, later.Batch.DuplicateCount));
        await using var db = CreateDbContext(user);
        Assert.Equal(3, await db.Transactions.CountAsync(CancellationToken));
    }

    [Fact]
    public async Task GapAfterThePreviousImport_IsAWarning()
    {
        var user = await NewUserAsync();
        await ImportAsync(user, Encode(FakeIngExport.File("2026-08-01", "2026-09-02", FakeIngExport.CardRow("2026-09-01", "-1,00", "'1'"))));

        var (batch, _) = await ImportAsync(user, Encode(FakeIngExport.File("2026-09-11", "2026-10-28", FakeIngExport.CardRow("2026-10-01", "-1,00", "'2'"))));

        var gap = Assert.Single(batch.Warnings);
        Assert.Equal(new ImportIssue(ImportIssueCodes.PeriodGap, From: new DateOnly(2026, 9, 3), To: new DateOnly(2026, 9, 10)), gap);
    }

    [Fact]
    public async Task RowOnAnUnlistedAccount_IsARowError()
    {
        var user = await NewUserAsync();
        var row = FakeIngExport.CardRow("2026-10-27", "-1,00", "'1'").Replace("KONTO Mobi pow.26", "Konto walutowe", StringComparison.Ordinal);

        var (batch, _) = await ImportAsync(user, Encode(FakeIngExport.File("2026-10-01", "2026-10-28", row, FakeIngExport.CardRow("2026-10-26", "-2,00", "'2'"))));

        Assert.Equal(1, batch.ErrorCount);
        Assert.Equal(1, batch.NewCount);
        Assert.Equal(new ImportIssue(ImportIssueCodes.UnknownAccount, LineNumber: 21, Account: "Konto walutowe"), Assert.Single(batch.Errors));
    }

    [Fact]
    public async Task TotalsMismatch_IsAWarning_NotAFix()
    {
        var user = await NewUserAsync();
        var text = FakeIngExport.File("2026-10-01", "2026-10-28", FakeIngExport.CardRow("2026-10-27", "-1,00", "'1'"))
            .Replace("\"Suma obciążeń (1):\";1,00", "\"Suma obciążeń (1):\";1,50", StringComparison.Ordinal);

        var (batch, _) = await ImportAsync(user, Encode(text));

        Assert.Equal(CheckResult.Failed, batch.TotalsCheck);
        Assert.Equal(ImportIssueCodes.TotalsSumMismatch, Assert.Single(batch.Warnings).Code);
        Assert.Equal(1, batch.NewCount);
    }

    [Fact]
    public async Task UnsupportedFile_IsRejected()
    {
        var user = await NewUserAsync();

        var error = await Assert.ThrowsAsync<StatementFormatException>(() => ImportAsync(user, "Date,Amount\n2026-01-01,-3.50\n"u8.ToArray()));

        Assert.Equal(ImportIssueCodes.UnsupportedFormat, error.Code);
    }
}
