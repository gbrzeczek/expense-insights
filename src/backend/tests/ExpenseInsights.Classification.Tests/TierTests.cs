using ExpenseInsights.Classification.Embeddings;
using ExpenseInsights.Classification.Tiers;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Classification.Tests;

/// <summary>Tiers A and C and the combining rule (plan 07), without the real model.</summary>
public sealed class TierTests
{
    private static readonly Guid Groceries = CategoryCatalog.Groceries.Id;
    private static readonly Guid Health = CategoryCatalog.Health.Id;
    private static readonly Guid Home = CategoryCatalog.Home.Id;

    [Fact]
    public void Memory_FindsTheLabelledMerchant_AndSaysWhich()
    {
        var memory = new MemoryClassifier([Example("kwiaciarnia roza", Home), Example("apteka slowik", Health)]);

        var result = memory.Classify(Card("kwiaciarnia rozana"));

        Assert.NotNull(result);
        Assert.Equal((Home, ClassificationSource.Similarity, Reasons.SimilarTo, "kwiaciarnia roza"), (result.CategoryId, result.Source, result.Reason, result.ReasonValue));
        Assert.InRange(result.Confidence, 0.5, 1);
    }

    [Fact]
    public void Memory_IsSureOfTheSameMerchant_AndUnsureOfASplitVote()
    {
        var memory = new MemoryClassifier([Example("sklep pod lipami", Groceries), Example("sklep pod lipami", Groceries), Example("sklep pod lipami", Home)]);

        var result = memory.Classify(Card("sklep pod lipami"))!;

        Assert.Equal(Groceries, result.CategoryId);
        Assert.Equal(2 / 3.0, result.Confidence, 3);
    }

    [Fact]
    public void Memory_HasNoOpinion_OnSomethingUnlike_OrInTheOtherDirection()
    {
        var memory = new MemoryClassifier([Example("biedronka", Groceries)]);

        Assert.Null(memory.Classify(Card("orlen")));
        Assert.Null(memory.Classify(Card("biedronka") with { Amount = 20m }));
        Assert.True(new MemoryClassifier([]).IsEmpty);
    }

    [Fact]
    public void Memory_TrustsANeighbourOfAnotherKindLess()
    {
        // A transfer to the person Anna Nowak says little about a card payment at "P.H.U. Anna Nowak".
        var memory = new MemoryClassifier([Example("anna nowak", CategoryCatalog.TransfersToPeople.Id, TransactionKind.BlikP2P)]);

        var result = memory.Classify(Card("anna nowak"))!;

        Assert.Equal(MemoryClassifier.OtherKindWeight, result.Confidence, 3);
        Assert.True(Confidence.NeedsReview(result.Source, result.Confidence));
    }

    [Fact]
    public async Task Embeddings_PickTheClosestAnchor_WithConfidenceFromTheMargin()
    {
        var embedder = new FakeEmbedder(new()
        {
            ["apteka"] = [1, 0, 0],
            ["kwiaciarnia"] = [0, 1, 0],
            ["apteka pod orlem"] = [0.98f, 0.2f, 0],
            ["nic podobnego"] = [0.6f, 0.6f, 0.53f],
        });
        using var anchors = new AnchorEmbeddings(embedder, [new CategoryAnchor(Health, "apteka", []), new CategoryAnchor(Home, "kwiaciarnia", [])]);
        var classifier = new EmbeddingClassifier(embedder, anchors, null);

        var results = await classifier.ClassifyAsync([Card("apteka pod orlem"), Card("nic podobnego"), Card("apteka pod orlem") with { Amount = 10m }], TestContext.Current.CancellationToken);

        Assert.Equal((Health, ClassificationSource.Embedding, Reasons.CategoryAnchor, "apteka"), (results[0]!.CategoryId, results[0]!.Source, results[0]!.Reason, results[0]!.ReasonValue));
        Assert.Equal(EmbeddingClassifier.MaxConfidence, results[0]!.Confidence);
        Assert.Equal(0, results[1]!.Confidence);
        Assert.Null(results[2]);
        Assert.Equal(2, embedder.Calls);
    }

    [Fact]
    public async Task Embeddings_UseTheLabelledNeighbours_InTheSameDirection()
    {
        var embedder = new FakeEmbedder(new() { ["sklepik"] = [1, 0, 0] });
        using var anchors = new AnchorEmbeddings(embedder, []);
        var neighbours = new InMemoryNeighbours(
        [
            (Example("sklepik", Groceries), [1f, 0, 0]),
            (Example("sklepik", CategoryCatalog.Refunds.Id) with { Amount = 5m }, [1f, 0, 0]),
        ]);

        var result = (await new EmbeddingClassifier(embedder, anchors, neighbours).ClassifyAsync([Card("sklepik")], TestContext.Current.CancellationToken))[0]!;

        Assert.Equal((Groceries, Reasons.SimilarTo, "sklepik"), (result.CategoryId, result.Reason, result.ReasonValue));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.015, 0.35)]
    [InlineData(EmbeddingClassifier.MarginForReview, Confidence.ReviewBelow)]
    [InlineData(0.2, EmbeddingClassifier.MaxConfidence)]
    public void EmbeddingConfidence_IsLinearInTheMargin(double margin, double expected) =>
        Assert.Equal(expected, EmbeddingClassifier.Calibrate(margin), 3);

    [Fact]
    public void Combine_AConfidentMemory_Wins()
    {
        var a = Tier(Groceries, 0.9, ClassificationSource.Similarity);

        Assert.Equal(Groceries, TierPipeline.Combine(null, a, Tier(Home, 0.95, ClassificationSource.Embedding))!.CategoryId);
    }

    [Fact]
    public void Combine_WhenAAndCAgree_TakesTheHigherConfidence()
    {
        var result = TierPipeline.Combine(null, Tier(Home, 0.5, ClassificationSource.Similarity), Tier(Home, 0.8, ClassificationSource.Embedding))!;

        Assert.Equal((Home, 0.8, ClassificationSource.Embedding), (result.CategoryId, result.Confidence, result.Source));
    }

    [Fact]
    public void Combine_WhenTheyDisagree_TheMoreConfident_ForReviewBelowTheThreshold()
    {
        var result = TierPipeline.Combine(null, Tier(Home, 0.5, ClassificationSource.Similarity), Tier(Health, 0.6, ClassificationSource.Embedding))!;

        Assert.Equal(Health, result.CategoryId);
        Assert.True(Confidence.NeedsReview(result.Source, result.Confidence));
    }

    [Fact]
    public void Combine_AWeakGuess_IsNoGuess_AndARuleGuessStaysUnlessATierIsBetter()
    {
        var rule = new RuleMatch(Groceries, ClassificationSource.GlobalRule, 0.5, Reasons.MerchantSimilar, "biedronka", null);

        Assert.Null(TierPipeline.Combine(null, Tier(Home, TierPipeline.MinGuess - 0.01, ClassificationSource.Similarity), null));
        Assert.Same(rule, TierPipeline.Combine(rule, Tier(Home, 0.45, ClassificationSource.Similarity), null));
        Assert.Equal(Home, TierPipeline.Combine(rule, Tier(Home, 0.75, ClassificationSource.Embedding), null)!.CategoryId);
    }

    [Fact]
    public async Task Pipeline_LeavesConfidentRules_AndOnlyAsksTheModelAboutWhatIsStillOpen()
    {
        var embedder = new FakeEmbedder(new() { ["apteka"] = [1, 0, 0], ["apteka pod orlem"] = [1, 0, 0], ["kwiaciarnia"] = [0, 1, 0] });
        using var anchors = new AnchorEmbeddings(embedder, [new CategoryAnchor(Health, "apteka", []), new CategoryAnchor(Home, "kwiaciarnia", [])]);
        var pipeline = new TierPipeline(new MemoryClassifier([Example("biedronka", Groceries)]), new EmbeddingClassifier(embedder, anchors, null));
        var rule = new RuleMatch(Home, ClassificationSource.GlobalRule, 0.95, Reasons.MerchantExact, "ikea", null);
        RuleInput[] inputs = [Card("ikea"), Card("biedronka"), Card("apteka pod orlem")];

        var results = await pipeline.ClassifyAsync(inputs, [[rule], [], []], TestContext.Current.CancellationToken);

        Assert.Same(rule, results[0]);
        Assert.Equal((Groceries, ClassificationSource.Similarity), (results[1]!.CategoryId, results[1]!.Source));
        Assert.Equal((Health, ClassificationSource.Embedding), (results[2]!.CategoryId, results[2]!.Source));
        Assert.DoesNotContain("query: biedronka", embedder.Texts);
        Assert.DoesNotContain("query: ikea", embedder.Texts);
    }

    [Fact]
    public void EveryCategoryWithMerchants_HasAnAnchor_OfMerchantKindsNotBrands()
    {
        var anchors = GlobalRules.Default.Anchors.ToDictionary(a => a.CategoryId);
        var spending = CategoryCatalog.All.Where(c => c.Kind == CategoryKind.Spending && c != CategoryCatalog.TransfersToPeople && c != CategoryCatalog.Other);

        Assert.All(spending, c => Assert.True(anchors.TryGetValue(c.Id, out var a) && a.Examples.Count >= 3, c.Key));

        // The harness measures the anchors on merchants they haven't seen: none is a shop of the fake data.
        var merchants = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "test-data", "fake_ing_history.labels.csv"))
            .Skip(1)
            .Select(l => l.Split(';'))
            .Where(f => f[5] is "card" or "blik")
            .Select(f => MerchantNormalizer.Fold(f[7]))
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(anchors.Values.SelectMany(a => a.Texts), t => Assert.DoesNotContain(MerchantNormalizer.Fold(t), merchants));
    }

    [Fact]
    public void TheProductionImage_DownloadsThePinnedModelFiles()
    {
        var dockerfile = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Dockerfile"));

        Assert.Contains(E5Model.Revision, dockerfile, StringComparison.Ordinal);
        Assert.All(E5Model.Files, f => Assert.Contains($"{f.Sha256}  {f.Name}", dockerfile, StringComparison.Ordinal));
    }

    private static RuleInput Card(string merchantKey) => new(TransactionKind.Card, -25m, merchantKey, "Płatność kartą", null);

    private static LabelledExample Example(string merchantKey, Guid categoryId, TransactionKind kind = TransactionKind.Card) => new(merchantKey, -25m, kind, categoryId);

    private static TierResult Tier(Guid categoryId, double confidence, ClassificationSource source) => new(categoryId, source, confidence, Reasons.SimilarTo, "x");

    /// <summary>Fixed vectors by text (after the <c>query: </c> prefix), normalized; anything else is a zero vector.</summary>
    private sealed class FakeEmbedder(Dictionary<string, float[]> vectors) : ITextEmbedder
    {
        public int Calls { get; private set; }

        public List<string> Texts { get; } = [];

        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        {
            Calls++;
            Texts.AddRange(texts);
            return Task.FromResult(texts.Select(t =>
            {
                var v = vectors.GetValueOrDefault(t["query: ".Length..], [0, 0, 0]);
                var norm = MathF.Sqrt(v.Sum(x => x * x));
                return norm == 0 ? v : v.Select(x => x / norm).ToArray();
            }).ToArray());
        }
    }
}
