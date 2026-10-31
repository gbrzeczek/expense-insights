using ExpenseInsights.Classification.Embeddings;
using ExpenseInsights.Classification.Tiers;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Classification;

/// <summary>
/// Tier C's neighbours from <c>pgvector</c>: the current user's labelled merchants nearest to an
/// embedding. Embeddings are stored per labelled merchant key; the missing ones (newly labelled, or
/// made by another model) are computed on the first search of a run, so the model only loads when
/// there is something to classify.
/// </summary>
internal sealed class StoredNeighbours(AppDbContext db, ITextEmbedder embedder, IReadOnlyList<LabelledExample> labelled, TimeProvider clock)
    : IEmbeddingNeighbours
{
    /// <summary>The shadow property holding the vector (Core doesn't know pgvector's type).</summary>
    internal const string VectorProperty = "Vector";

    // Per merchant key and direction: whether it was a payment, and its most frequent category.
    private readonly Dictionary<(string Key, bool Outgoing), (bool Payment, Guid CategoryId)> _labels = labelled
        .Where(e => e.Amount != 0 && e.MerchantKey.Length > 0)
        .GroupBy(e => (e.MerchantKey, e.Amount < 0))
        .ToDictionary(g => g.Key, g => (
            g.GroupBy(e => LabelledExample.IsPayment(e.Kind)).MaxBy(p => p.Count())!.Key,
            g.GroupBy(e => e.CategoryId).MaxBy(c => c.Count())!.Key));

    private bool _ready;

    public async Task<IReadOnlyList<EmbeddingNeighbour>> NearestAsync(float[] vector, bool outgoing, int count, CancellationToken ct)
    {
        if (!_ready)
        {
            await BackfillAsync(ct);
            _ready = true;
        }

        var keys = _labels.Keys.Where(k => k.Outgoing == outgoing).Select(k => k.Key).ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        var query = new Vector(vector);
        var near = await db.MerchantEmbeddings.AsNoTracking()
            .Where(e => e.Model == ClassificationTiers.Model && keys.Contains(e.MerchantKey))
            .OrderBy(e => EF.Property<Vector>(e, VectorProperty).CosineDistance(query))
            .Take(count)
            .Select(e => new { e.MerchantKey, Distance = EF.Property<Vector>(e, VectorProperty).CosineDistance(query) })
            .ToListAsync(ct);
        return [.. near.Select(n =>
        {
            var (payment, category) = _labels[(n.MerchantKey, outgoing)];
            return new EmbeddingNeighbour(n.MerchantKey, category, payment, 1 - n.Distance);
        })];
    }

    /// <summary>Embeds the labelled merchant keys that have no embedding from the current model yet.</summary>
    private async Task BackfillAsync(CancellationToken ct)
    {
        var keys = _labels.Keys.Select(k => k.Key).Distinct(StringComparer.Ordinal).ToList();
        var stored = await db.MerchantEmbeddings
            .Where(e => e.Model == ClassificationTiers.Model && keys.Contains(e.MerchantKey))
            .Select(e => e.MerchantKey)
            .ToListAsync(ct);
        var missing = keys.Except(stored, StringComparer.Ordinal).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var vectors = await embedder.EmbedAsync([.. missing.Select(EmbeddingText.For)], ct);
        var now = clock.GetUtcNow();
        for (var i = 0; i < missing.Count; i++)
        {
            var embedding = new MerchantEmbedding { Id = Guid.CreateVersion7(), MerchantKey = missing[i], Model = ClassificationTiers.Model, CreatedAt = now };
            db.MerchantEmbeddings.Add(embedding);
            db.Entry(embedding).Property(VectorProperty).CurrentValue = new Vector(vectors[i]);
        }

        // Older models' embeddings are no use any more.
        await db.MerchantEmbeddings.Where(e => e.Model != ClassificationTiers.Model).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
    }
}
