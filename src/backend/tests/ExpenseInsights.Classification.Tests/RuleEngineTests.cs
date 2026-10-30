using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Classification.Tests;

public sealed class RuleEngineTests
{
    private static readonly MerchantRule[] NoRules = [];

    [Theory]
    [InlineData("biedronka", "groceries", Reasons.MerchantExact)]
    [InlineData("apteka doz", "health", Reasons.MerchantContains)]
    [InlineData("uber eats", "restaurants", Reasons.MerchantExact)]
    [InlineData("uber", "transport", Reasons.MerchantExact)]
    [InlineData("youtubepremium", "subscriptions", Reasons.MerchantContains)]
    [InlineData("bp", "transport", Reasons.MerchantExact)]
    public void GlobalMerchantRules_ClassifyCardPayments(string key, string category, string reason)
    {
        var best = Best(Card(key));

        Assert.Equal(CategoryCatalog.ByKey(category).Id, best.CategoryId);
        Assert.Equal(ClassificationSource.GlobalRule, best.Source);
        Assert.Equal(reason, best.Reason);
        Assert.False(Confidence.NeedsReview(best.Source, best.Confidence));
    }

    [Fact]
    public void Contains_OnlyMatchesTheStartOfAWord() =>
        Assert.DoesNotContain(Evaluate(Card("sklep obiadowy")), m => m.CategoryId == CategoryCatalog.Home.Id);

    [Fact]
    public void AMisspeltMerchant_IsASimilarMatchForReview()
    {
        var best = Best(Card("mediaexpet"));

        Assert.Equal(CategoryCatalog.Electronics.Id, best.CategoryId);
        Assert.Equal(Reasons.MerchantSimilar, best.Reason);
        Assert.True(Confidence.NeedsReview(best.Source, best.Confidence));
    }

    [Fact]
    public void AnUnknownMerchant_IsUnclassified() => Assert.Empty(Evaluate(Card("krakow zakopian")));

    [Fact]
    public void OwnTransfer_IsExcludedWithCertainty()
    {
        var best = Best(new RuleInput(TransactionKind.OwnTransfer, -2000m, "jan pawel kowalski", "Przelew własny - oszczędności", "hash"));

        Assert.Equal(CategoryCatalog.OwnTransfer.Id, best.CategoryId);
        Assert.Equal(1.0, best.Confidence);
    }

    [Fact]
    public void FromTheAccountHolderWithAnUnknownAccount_IsIncomeForReview()
    {
        // E.g. from the user's business account, which isn't in the export.
        var input = new RuleInput(TransactionKind.Transfer, 5000m, "jan pawel kowalski", "Wypłata z firmy", "business");

        var best = Best(input, holder: "Jan Kowalski");

        Assert.Equal(CategoryCatalog.OtherIncome.Id, best.CategoryId);
        Assert.Equal(Reasons.AccountHolderIncoming, best.Reason);
        Assert.True(Confidence.NeedsReview(best.Source, best.Confidence));
    }

    [Fact]
    public void ToTheAccountHolderWithAnUnknownAccount_IsAnOwnTransferForReview()
    {
        var best = Best(new RuleInput(TransactionKind.Transfer, -500m, "jan pawel kowalski", "Na konto w innym banku", "other"), holder: "Jan Kowalski");

        Assert.Equal(CategoryCatalog.OwnTransfer.Id, best.CategoryId);
        Assert.True(Confidence.NeedsReview(best.Source, best.Confidence));
    }

    [Fact]
    public void AUserRuleByAccount_BeatsTheAccountHolderGuess()
    {
        var rule = Rule(RuleMatchType.CounterpartyAccount, "business", CategoryCatalog.Salary.Id, label: "Moja firma");
        var input = new RuleInput(TransactionKind.Transfer, 5000m, "jan pawel kowalski", "Wypłata z firmy", "business");

        var best = Best(input, [rule], "Jan Kowalski");

        Assert.Equal(CategoryCatalog.Salary.Id, best.CategoryId);
        Assert.Equal(ClassificationSource.UserRule, best.Source);
        Assert.Equal(rule.Id, best.RuleId);
        Assert.Equal("Moja firma", best.ReasonValue);
    }

    [Fact]
    public void ASingleNameWord_IsNotTheAccountHolder() =>
        Assert.False(RuleEngine.IsAccountHolder("jan nowak", RuleEngine.NameWords("Jan")));

    [Theory]
    [InlineData(TransactionKind.PhoneTransfer, -93, "transfersToPeople")]
    [InlineData(TransactionKind.BlikP2P, -20, "transfersToPeople")]
    [InlineData(TransactionKind.BlikP2P, 22, "transfersFromPeople")]
    public void TransfersBetweenPeople_GoToTheHoldingCategories(TransactionKind kind, int amount, string category) =>
        Assert.Equal(CategoryCatalog.ByKey(category).Id, Best(new RuleInput(kind, amount, "anna nowak", "pizza", "x")).CategoryId);

    [Fact]
    public void AUserRuleByRecipient_BreaksDownTransfersToPeople()
    {
        var rent = Rule(RuleMatchType.CounterpartyAccount, "landlord", CategoryCatalog.Bills.Id);

        var best = Best(new RuleInput(TransactionKind.PhoneTransfer, -2500m, "anna nowak", "czynsz", "landlord"), [rent]);

        Assert.Equal(CategoryCatalog.Bills.Id, best.CategoryId);
    }

    [Fact]
    public void APositiveCardPayment_IsARefund() =>
        Assert.Equal(CategoryCatalog.Refunds.Id, Best(new RuleInput(TransactionKind.Card, 129.99m, "zalando", "Płatność kartą", null)).CategoryId);

    [Fact]
    public void MoneyBackFromAKnownMerchant_IsARefund() =>
        Assert.Equal(CategoryCatalog.Refunds.Id, Best(new RuleInput(TransactionKind.Transfer, 80m, "tauron sprzedaz", "Nadpłata", "x")).CategoryId);

    [Theory]
    [InlineData(10000, "WYNAGRODZENIE ZA 09/2026", "salary")]
    [InlineData(-742.18, "CZYNSZ 10/2026 LOK. 15", "bills")]
    [InlineData(41.2, "Kapitalizacja odsetek", "interest")]
    [InlineData(-7.83, "Podatek od odsetek", "interest")]
    public void TitleKeywords_ClassifyTransfers(double amount, string title, string category) =>
        Assert.Equal(CategoryCatalog.ByKey(category).Id, Best(new RuleInput(TransactionKind.Transfer, (decimal)amount, "acme software", title, "x")).CategoryId);

    [Fact]
    public void AnOutgoingTransferTitledSalary_IsNotIncome() =>
        Assert.DoesNotContain(Evaluate(new RuleInput(TransactionKind.Transfer, -300m, "jan nowak", "wynagrodzenie za sprzątanie", "x")), m => m.CategoryId == CategoryCatalog.Salary.Id);

    [Fact]
    public void AUserRule_BeatsTheGlobalOne()
    {
        var rule = Rule(RuleMatchType.Exact, "biedronka", CategoryCatalog.Home.Id);

        var matches = Evaluate(Card("biedronka"), [rule]);

        Assert.Equal(CategoryCatalog.Home.Id, matches[0].CategoryId);
        Assert.Equal(CategoryCatalog.Groceries.Id, matches[1].CategoryId);
    }

    [Fact]
    public void Matches_AreOnePerCategory()
    {
        var rule = Rule(RuleMatchType.Contains, "bied", CategoryCatalog.Groceries.Id);

        Assert.Single(Evaluate(Card("biedronka"), [rule]));
    }

    [Fact]
    public void GlobalRules_AllPointAtCatalogCategories()
    {
        var ids = CategoryCatalog.All.Select(c => c.Id).ToHashSet();

        Assert.True(GlobalRules.Default.Rules.Count >= 150);
        Assert.All(GlobalRules.Default.Rules, r => Assert.Contains(r.CategoryId, ids));
    }

    [Fact]
    public void GlobalRules_RejectPatternsThatCanNeverMatch() =>
        Assert.Throws<InvalidOperationException>(() => GlobalRules.Parse("""{ "categories": { "groceries": { "contains": ["Żabka"] } } }"""));

    private static RuleInput Card(string key) => new(TransactionKind.Card, -25m, key, "Płatność kartą 25.10.2026 Nr karty 4111xx1234", null);

    private static MerchantRule Rule(RuleMatchType type, string pattern, Guid categoryId, string? label = null) =>
        new() { Id = Guid.NewGuid(), MatchType = type, Pattern = pattern, CategoryId = categoryId, Label = label };

    private static IReadOnlyList<RuleMatch> Evaluate(RuleInput input, MerchantRule[]? rules = null, string? holder = null) =>
        RuleEngine.Evaluate(input, rules ?? NoRules, GlobalRules.Default, RuleEngine.NameWords(holder));

    private static RuleMatch Best(RuleInput input, MerchantRule[]? rules = null, string? holder = null) =>
        Evaluate(input, rules, holder)[0];
}
