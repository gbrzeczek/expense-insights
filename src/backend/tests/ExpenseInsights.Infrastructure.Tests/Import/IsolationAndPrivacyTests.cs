using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Testing;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Tests.Import;

public sealed class IsolationAndPrivacyTests(PostgresFixture postgres) : ImportTestBase(postgres)
{
    [Fact]
    public async Task UserOwnedEntities_AreInvisibleToOtherUsers()
    {
        var owner = await NewUserAsync();
        var other = await NewUserAsync();
        await ImportAsync(owner, "fake_ing.csv");

        await using var db = CreateDbContext(other);

        Assert.Empty(await db.Accounts.ToListAsync(CancellationToken));
        Assert.Empty(await db.Transactions.ToListAsync(CancellationToken));
        Assert.Empty(await db.ImportBatches.ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task UserOwnedEntities_OfAnotherUser_CantBeSaved()
    {
        var owner = await NewUserAsync();
        var other = await NewUserAsync();
        await ImportAsync(owner, "fake_ing.csv");
        await using var ownerDb = CreateDbContext(owner);
        var account = await ownerDb.Accounts.AsNoTracking().FirstAsync(CancellationToken);
        var transaction = await ownerDb.Transactions.AsNoTracking().FirstAsync(CancellationToken);
        var batch = await ownerDb.ImportBatches.AsNoTracking().FirstAsync(CancellationToken);

        foreach (var entity in new object[] { account, transaction, batch })
        {
            await using var db = CreateDbContext(other);
            db.Update(entity);

            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(CancellationToken));
        }
    }

    [Fact]
    public async Task SameFile_ImportsSeparatelyForEachUser()
    {
        var first = await NewUserAsync();
        var second = await NewUserAsync();
        await ImportAsync(first, "fake_ing.csv");

        var (batch, alreadyImported) = await ImportAsync(second, "fake_ing.csv");

        Assert.False(alreadyImported);
        Assert.Equal(335, batch.NewCount);
    }

    [Fact]
    public async Task PreamblePii_AppearsNowhereInTheDatabaseOrLogs()
    {
        var user = await NewUserAsync();

        await ImportAsync(user, "fake_ing.csv");

        await using var db = CreateDbContext(user);
        var dump = string.Join('\n', await db.Database.SqlQuery<string>($"""
            SELECT row_to_json(t)::text AS "Value" FROM "Transactions" t
            UNION ALL SELECT row_to_json(a)::text FROM "Accounts" a
            UNION ALL SELECT row_to_json(b)::text FROM "ImportBatches" b
            """).ToListAsync(CancellationToken));
        var logs = string.Join('\n', Logs.Messages);
        Assert.Contains("KONTO Mobi pow.26", dump, StringComparison.Ordinal);
        Assert.NotEmpty(Logs.Messages);
        foreach (var text in new[] { dump, logs })
        {
            Assert.DoesNotContain(FakeIngExport.PreamblePii, text, StringComparison.Ordinal);
            Assert.DoesNotContain("FAŁSZYWA", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("00-001", text, StringComparison.Ordinal);
            Assert.DoesNotContain("KOWALSKI JAN PAWEŁ", text, StringComparison.Ordinal);
        }

        // Hard rule 9: the user's own account numbers aren't logged either.
        Assert.DoesNotContain("12104332181960013389083863", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("1043 3218", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnAccountNumbers_AreStoredOnlyAsMaskAndHash()
    {
        var user = await NewUserAsync();

        await ImportAsync(user, "fake_ing.csv");

        await using var db = CreateDbContext(user);
        var dump = string.Join('\n', await db.Database.SqlQuery<string>($"SELECT row_to_json(a)::text AS \"Value\" FROM \"Accounts\" a").ToListAsync(CancellationToken));
        Assert.DoesNotContain("1043 3218", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("12104332181960013389083863", dump, StringComparison.Ordinal);
        Assert.Contains(AccountNumber.Hash("12104332181960013389083863")!, dump, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrigramSimilarity_MatchesPostgres()
    {
        await using var db = CreateDbContext();
        (string, string)[] pairs =
        [
            ("word", "two words"),
            ("ZABKA Z5017 K.1  KRAKOW  POL", "ZABKA Z5017 K.1 KRAKOW PO"),
            ("JMP S.A. BIEDRONKA 6537 KRAKOW PO", "JMP S.A. BIEDRONKA 3803 KRAKOW PO"),
            ("Bershka.com Arteixo", "LIDL KAMIENSKIEGO KRAKOW POL"),
            ("Zielińska ŚLĄSKI", "zielinska slaski"),
        ];

        foreach (var (a, b) in pairs)
        {
            var expected = await db.Database.SqlQuery<double>($"SELECT similarity({a}, {b})::float8 AS \"Value\"").SingleAsync(CancellationToken);

            Assert.Equal(expected, TrigramSimilarity.Similarity(a, b), 5);
        }
    }

    [Fact]
    public async Task Transactions_KeepMoneyExact()
    {
        var user = await NewUserAsync();

        await ImportAsync(user, "fake_ing.csv");

        await using var db = CreateDbContext(user);
        var debits = await db.Transactions.Where(t => t.Amount < 0).SumAsync(t => t.Amount, CancellationToken);
        Assert.Equal(-42189.51m, debits);
        Assert.Equal(4, await db.Transactions.CountAsync(t => t.Status == TransactionStatus.Pending && t.ExternalId == null, CancellationToken));
    }
}
