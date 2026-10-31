using ExpenseInsights.Classification;
using ExpenseInsights.Classification.Embeddings;
using ExpenseInsights.Classification.Tiers;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Infrastructure.Classification;
using ExpenseInsights.Infrastructure.Tests.Import;
using ExpenseInsights.Testing;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Tests.Classification;

/// <summary>The tiers after the rules (plan 07) in the pipeline, on <c>fake_ing.csv</c>.</summary>
public sealed class TierClassificationTests(PostgresFixture postgres) : ImportTestBase(postgres)
{
    // "9179 KRAKOW ZAKOPIAN": no rule knows it, so it stays unclassified until the user says.
    private const string Unknown = "krakow zakopian";

    [Fact]
    public async Task AfterOneCorrection_TheMemoryClassifiesTheSameMerchant_AndNeverTouchesTheCorrection()
    {
        var user = await ImportAndClassifyAsync(ClassificationTiers.RulesAndMemory);
        var rows = await RowsAsync(user, Unknown);
        Assert.All(rows, t => Assert.Null(t.CategoryId));

        await using (var db = CreateDbContext(user))
        {
            var classifier = new TransactionClassifier(db, TimeProvider.System);
            await new Corrections(db, classifier, TimeProvider.System).CorrectAsync(rows[0].Id, CategoryCatalog.Other.Id, CancellationToken);
            await classifier.ClassifyAsync(null, CancellationToken);
        }

        var after = await RowsAsync(user, Unknown);
        Assert.Equal(ClassificationSource.UserCorrection, after[0].ClassificationSource);
        Assert.All(after.Skip(1), t =>
        {
            Assert.Equal((CategoryCatalog.Other.Id, ClassificationSource.Similarity, Reasons.SimilarTo, Unknown), (t.CategoryId, t.ClassificationSource, t.ClassificationReason, t.ClassificationReasonValue));
            Assert.False(Confidence.NeedsReview(t.ClassificationSource, t.ClassificationConfidence));
        });

        // Reclassifying again changes nothing, and still leaves the correction alone.
        await using (var db = CreateDbContext(user))
        {
            Assert.Equal(0, await new TransactionClassifier(db, TimeProvider.System).ClassifyAsync(null, CancellationToken));
        }

        Assert.Equal(ClassificationSource.UserCorrection, (await RowsAsync(user, Unknown))[0].ClassificationSource);
    }

    [Fact]
    public async Task Embeddings_AreStoredForLabelledMerchants_AndFoundAsNeighbours()
    {
        using var tiers = EmbeddingsOnly();
        var embedder = (BagOfWords)tiers.Embedder!;
        var user = await ImportAndClassifyAsync(tiers);
        var rows = await RowsAsync(user, Unknown);
        await using (var db = CreateDbContext(user))
        {
            // Nothing labelled yet: open transactions are compared with the anchors, nothing is stored.
            Assert.Empty(await db.MerchantEmbeddings.ToListAsync(CancellationToken));
        }

        await using (var db = CreateDbContext(user))
        {
            var classifier = new TransactionClassifier(db, TimeProvider.System, tiers);
            await new Corrections(db, classifier, TimeProvider.System).CorrectAsync(rows[0].Id, CategoryCatalog.Other.Id, CancellationToken);
            await classifier.ClassifyAsync(null, CancellationToken);
        }

        await using var check = CreateDbContext(user);
        var stored = await check.MerchantEmbeddings.Select(e => new { e.MerchantKey, e.Model }).ToListAsync(CancellationToken);
        Assert.Equal([new { MerchantKey = Unknown, Model = ClassificationTiers.Model }], stored);
        var after = await RowsAsync(user, Unknown);
        Assert.All(after.Skip(1), t =>
            Assert.Equal((CategoryCatalog.Other.Id, ClassificationSource.Embedding, Reasons.SimilarTo, Unknown), (t.CategoryId, t.ClassificationSource, t.ClassificationReason, t.ClassificationReasonValue)));

        // A second run reuses the stored embedding: only the open merchant is embedded, once for its five rows.
        var calls = embedder.Texts.Count(t => t == EmbeddingText.For(Unknown));
        await new TransactionClassifier(check, TimeProvider.System, tiers).ClassifyAsync(null, CancellationToken);
        Assert.Equal(calls + 1, embedder.Texts.Count(t => t == EmbeddingText.For(Unknown)));
    }

    [Fact]
    public async Task MerchantEmbeddings_AreInvisibleToOtherUsers_AndCantBeSavedByThem()
    {
        using var tiers = EmbeddingsOnly();
        var owner = await ImportAndClassifyAsync(tiers);
        var other = await NewUserAsync();
        await using (var db = CreateDbContext(owner))
        {
            await new Corrections(db, new TransactionClassifier(db, TimeProvider.System, tiers), TimeProvider.System).LabelMerchantAsync(Unknown, CategoryCatalog.Other.Id, CancellationToken);
            await new TransactionClassifier(db, TimeProvider.System, tiers).ClassifyAsync(null, CancellationToken);
            Assert.NotEmpty(await db.MerchantEmbeddings.ToListAsync(CancellationToken));
        }

        await using var ownerDb = CreateDbContext(owner);
        var embedding = await ownerDb.MerchantEmbeddings.AsNoTracking().FirstAsync(CancellationToken);
        await using var otherDb = CreateDbContext(other);
        Assert.Empty(await otherDb.MerchantEmbeddings.ToListAsync(CancellationToken));
        otherDb.Update(embedding);
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherDb.SaveChangesAsync(CancellationToken));
    }

    private async Task<Guid> ImportAndClassifyAsync(ClassificationTiers tiers)
    {
        var user = await NewUserAsync();
        await ImportAsync(user, "fake_ing.csv");
        await using var db = CreateDbContext(user);
        await new TransactionClassifier(db, TimeProvider.System, tiers).ClassifyAsync(null, CancellationToken);
        return user;
    }

    private async Task<List<Transaction>> RowsAsync(Guid user, string merchantKey)
    {
        await using var db = CreateDbContext(user);
        return await db.Transactions.AsNoTracking().Where(t => t.MerchantKey == merchantKey).OrderBy(t => t.TransactionDate).ThenBy(t => t.Id).ToListAsync(CancellationToken);
    }

    /// <summary>Tier C alone, with a stand-in model and no anchors, so the neighbours are what's tested.</summary>
    private static ClassificationTiers EmbeddingsOnly()
    {
        var embedder = new BagOfWords();
        return new ClassificationTiers(false, embedder, new AnchorEmbeddings(embedder, []));
    }

    /// <summary>A stand-in for the model: one dimension per word, so texts sharing words are similar.</summary>
    private sealed class BagOfWords : ITextEmbedder
    {
        public List<string> Texts { get; } = [];

        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        {
            Texts.AddRange(texts);
            return Task.FromResult(texts.Select(text =>
            {
                var vector = new float[E5Model.Dimensions];
                foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    vector[(int)((uint)StringComparer.Ordinal.GetHashCode(word) % E5Model.Dimensions)] += 1;
                }

                var norm = MathF.Sqrt(vector.Sum(v => v * v));
                return vector.Select(v => v / norm).ToArray();
            }).ToArray());
        }
    }
}
