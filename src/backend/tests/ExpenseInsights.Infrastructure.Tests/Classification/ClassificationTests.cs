using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Infrastructure.Classification;
using ExpenseInsights.Infrastructure.Persistence;
using ExpenseInsights.Infrastructure.Tests.Import;
using ExpenseInsights.Testing;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Tests.Classification;

public sealed class ClassificationTests(PostgresFixture postgres) : ImportTestBase(postgres)
{
    [Fact]
    public async Task AfterImport_RulesClassifyMostTransactions()
    {
        var user = await ImportAndClassifyAsync();

        await using var db = CreateDbContext(user);
        var all = await db.Transactions.CountAsync(CancellationToken);
        var classified = await db.Transactions.CountAsync(t => t.CategoryId != null, CancellationToken);
        Assert.True(classified >= all * 0.9, $"{classified} of {all}");
        Assert.Equal(0, await db.Transactions.CountAsync(t => t.ClassifiedAt == null, CancellationToken));
        Assert.Equal(classified, await db.ClassificationHistory.CountAsync(CancellationToken));

        var biedronka = await db.Transactions.Where(t => t.MerchantKey == "biedronka").ToListAsync(CancellationToken);
        Assert.NotEmpty(biedronka);
        Assert.All(biedronka, t => Assert.Equal(CategoryCatalog.Groceries.Id, t.CategoryId));
        Assert.Equal(12, await db.Transactions.CountAsync(t => t.CategoryId == CategoryCatalog.OwnTransfer.Id, CancellationToken));
    }

    [Fact]
    public async Task Reclassifying_ChangesNothing_AndNeverOverwritesACorrection()
    {
        var user = await ImportAndClassifyAsync();
        var lidl = await FirstAsync(user, "lidl kamienskiego");
        await using (var db = CreateDbContext(user))
        {
            await CorrectionsFor(db).CorrectAsync(lidl.Id, CategoryCatalog.Restaurants.Id, CancellationToken);
        }

        await using (var db = CreateDbContext(user))
        {
            Assert.Equal(0, await new TransactionClassifier(db, TimeProvider.System).ClassifyAsync(null, CancellationToken));
        }

        var after = await GetAsync(user, lidl.Id);
        Assert.Equal(CategoryCatalog.Restaurants.Id, after.CategoryId);
        Assert.Equal(ClassificationSource.UserCorrection, after.ClassificationSource);
    }

    [Fact]
    public async Task ApplyToAll_RecategorizesTheMerchant_ExceptOtherCorrections_AndFutureImportsFollow()
    {
        var user = await ImportAndClassifyAsync();
        await using var db = CreateDbContext(user);
        var zabka = await db.Transactions.AsNoTracking().Where(t => t.MerchantKey == "zabka").OrderBy(t => t.Id).ToListAsync(CancellationToken);
        var corrections = CorrectionsFor(db);
        await corrections.CorrectAsync(zabka[1].Id, CategoryCatalog.Other.Id, CancellationToken);
        var corrected = await corrections.CorrectAsync(zabka[0].Id, CategoryCatalog.Restaurants.Id, CancellationToken);

        var offer = await corrections.OfferAsync(corrected, CancellationToken);
        var (rule, changed) = await corrections.ApplyToAllAsync(zabka[0].Id, CancellationToken);

        Assert.Equal(new ApplyToAllOffer(RuleMatchType.Exact, "zabka", zabka.Count - 2), offer);
        Assert.Equal(zabka.Count - 2, changed);
        Assert.Equal(("zabka", CategoryCatalog.Restaurants.Id), (rule.Pattern, rule.CategoryId));
        await using var check = CreateDbContext(user);
        var now = await check.Transactions.Where(t => t.MerchantKey == "zabka").ToDictionaryAsync(t => t.Id, CancellationToken);
        Assert.Equal(CategoryCatalog.Other.Id, now[zabka[1].Id].CategoryId);
        Assert.Equal(zabka.Count - 1, now.Values.Count(t => t.CategoryId == CategoryCatalog.Restaurants.Id));
        Assert.Contains(now.Values, t => t.ClassificationSource == ClassificationSource.UserRule && t.ClassificationRuleId == rule.Id);

        // A new export with a Żabka payment: the rule applies.
        var file = FakeIngExport.File("2026-11-01", "2026-11-02", FakeIngExport.CardRow("2026-11-02", "-12,50", "'202630697000000001'", " ZABKA Z9999 K.1  WARSZAWA  POL "));
        await ImportAsync(user, Encode(file), "nowy.csv");
        await ClassifyAsync(user);
        Assert.Equal(CategoryCatalog.Restaurants.Id, (await FirstAsync(user, "zabka", t => t.TransactionDate == new DateOnly(2026, 11, 2))).CategoryId);
    }

    [Fact]
    public async Task ApplyToAll_ForAPerson_MatchesTheirAccount_WithoutStoringTheNumber()
    {
        var user = await ImportAndClassifyAsync();
        var transfer = await FirstAsync(user, "michal wojcik", t => t.Kind == TransactionKind.PhoneTransfer);
        await using var db = CreateDbContext(user);
        var corrections = CorrectionsFor(db);
        await corrections.CorrectAsync(transfer.Id, CategoryCatalog.Restaurants.Id, CancellationToken);

        var (rule, _) = await corrections.ApplyToAllAsync(transfer.Id, CancellationToken);

        Assert.Equal(RuleMatchType.CounterpartyAccount, rule.MatchType);
        Assert.Equal("Michał Wójcik", rule.Label);
        Assert.DoesNotContain(transfer.CounterpartyAccount!.Trim('\''), rule.Pattern, StringComparison.Ordinal);
        Assert.Equal(64, rule.Pattern.Length);
    }

    [Fact]
    public async Task DeletingARule_ReclassifiesWhatItHadClassified()
    {
        var user = await ImportAndClassifyAsync();
        await using var db = CreateDbContext(user);
        var corrections = CorrectionsFor(db);
        var (rule, _) = await corrections.LabelMerchantAsync("rossmann", CategoryCatalog.Health.Id, CancellationToken);
        Assert.All(await db.Transactions.AsNoTracking().Where(t => t.MerchantKey == "rossmann").ToListAsync(CancellationToken), t => Assert.Equal(CategoryCatalog.Health.Id, t.CategoryId));

        var changed = await corrections.DeleteRuleAsync(rule.Id, CancellationToken);

        Assert.True(changed > 0);
        await using var check = CreateDbContext(user);
        Assert.All(await check.Transactions.Where(t => t.MerchantKey == "rossmann").ToListAsync(CancellationToken), t => Assert.Equal(CategoryCatalog.Beauty.Id, t.CategoryId));
        Assert.Empty(await check.MerchantRules.ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task Offsets_OnlyForIncomingMoneyAndSpendingCategories()
    {
        var user = await ImportAndClassifyAsync();
        var incoming = await FirstAsync(user, "katarzyna zielinska", t => t.Amount > 0);
        var outgoing = await FirstAsync(user, "biedronka");
        await using var db = CreateDbContext(user);
        var corrections = CorrectionsFor(db);

        var marked = await corrections.SetOffsetsAsync(incoming.Id, CategoryCatalog.Groceries.Id, CancellationToken);

        Assert.Equal(CategoryCatalog.Groceries.Id, marked.OffsetsCategoryId);
        Assert.Equal(Infrastructure.Classification.Corrections.NotIncoming, (await Assert.ThrowsAsync<CorrectionException>(() => corrections.SetOffsetsAsync(outgoing.Id, CategoryCatalog.Groceries.Id, CancellationToken))).Code);
        Assert.Equal(Infrastructure.Classification.Corrections.NotSpending, (await Assert.ThrowsAsync<CorrectionException>(() => corrections.SetOffsetsAsync(incoming.Id, CategoryCatalog.Salary.Id, CancellationToken))).Code);
        Assert.Null((await corrections.SetOffsetsAsync(incoming.Id, null, CancellationToken)).OffsetsCategoryId);
    }

    [Fact]
    public async Task ACorrectionMadeMeanwhile_StopsAStaleWrite()
    {
        var user = await ImportAndClassifyAsync();
        var lidl = await FirstAsync(user, "lidl kamienskiego");
        await using var stale = CreateDbContext(user);
        var staleCopy = await stale.Transactions.SingleAsync(t => t.Id == lidl.Id, CancellationToken);

        await using (var db = CreateDbContext(user))
        {
            await CorrectionsFor(db).CorrectAsync(lidl.Id, CategoryCatalog.Other.Id, CancellationToken);
        }

        staleCopy.CategoryId = CategoryCatalog.Health.Id;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync(CancellationToken));
    }

    [Fact]
    public async Task ClassificationData_IsInvisibleAndReadOnlyToOtherUsers()
    {
        var owner = await ImportAndClassifyAsync();
        var other = await NewUserAsync();
        Category own;
        await using (var db = CreateDbContext(owner))
        {
            await CorrectionsFor(db).LabelMerchantAsync("biedronka", CategoryCatalog.Home.Id, CancellationToken);
            own = new Category { Id = Guid.CreateVersion7(), Name = "Kawa", ParentId = CategoryCatalog.Restaurants.Id, Kind = CategoryKind.Spending, CreatedAt = DateTimeOffset.UtcNow };
            db.Categories.Add(own);
            db.CategorySettings.Add(new CategorySetting { Id = Guid.CreateVersion7(), CategoryId = CategoryCatalog.Other.Id, IsHidden = true, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(CancellationToken);
            Assert.Equal(owner, own.UserId);
        }

        await using var otherDb = CreateDbContext(other);
        Assert.Empty(await otherDb.MerchantRules.ToListAsync(CancellationToken));
        Assert.Empty(await otherDb.CategorySettings.ToListAsync(CancellationToken));
        Assert.Empty(await otherDb.ClassificationHistory.ToListAsync(CancellationToken));
        Assert.Equal(CategoryCatalog.All.Count, await otherDb.Categories.CountAsync(CancellationToken));

        otherDb.Update(own);
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherDb.SaveChangesAsync(CancellationToken));
        otherDb.ChangeTracker.Clear();
        var global = await otherDb.Categories.SingleAsync(c => c.Id == CategoryCatalog.Groceries.Id, CancellationToken);
        global.SortOrder = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherDb.SaveChangesAsync(CancellationToken));
        otherDb.ChangeTracker.Clear();
        var ownersLidl = await FirstAsync(owner, "lidl kamienskiego");
        await Assert.ThrowsAsync<CorrectionException>(() => CorrectionsFor(otherDb).CorrectAsync(ownersLidl.Id, CategoryCatalog.Other.Id, CancellationToken));
        await Assert.ThrowsAsync<CorrectionException>(() => CorrectionsFor(otherDb).LabelMerchantAsync("biedronka", own.Id, CancellationToken));
    }

    private async Task<Guid> ImportAndClassifyAsync()
    {
        var user = await NewUserAsync();
        await ImportAsync(user, "fake_ing.csv");
        await ClassifyAsync(user);
        return user;
    }

    private async Task ClassifyAsync(Guid user)
    {
        await using var db = CreateDbContext(user);
        await new TransactionClassifier(db, TimeProvider.System).ClassifyAsync(null, CancellationToken);
    }

    private static Corrections CorrectionsFor(AppDbContext db) => new(db, new TransactionClassifier(db, TimeProvider.System), TimeProvider.System);

    private async Task<Transaction> FirstAsync(Guid user, string merchantKey, Func<Transaction, bool>? where = null)
    {
        await using var db = CreateDbContext(user);
        var all = await db.Transactions.AsNoTracking().Where(t => t.MerchantKey == merchantKey).OrderBy(t => t.Id).ToListAsync(CancellationToken);
        return all.First(where ?? (_ => true));
    }

    private async Task<Transaction> GetAsync(Guid user, Guid id)
    {
        await using var db = CreateDbContext(user);
        return await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == id, CancellationToken);
    }
}
