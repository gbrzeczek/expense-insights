using ExpenseInsights.Classification.Embeddings;
using ExpenseInsights.Core.Classification;

namespace ExpenseInsights.Classification.Tiers;

/// <summary>One of the user's labelled transactions near the one being classified.</summary>
/// <param name="MerchantKey">For the explanation.</param>
/// <param name="CategoryId">Its confirmed category.</param>
/// <param name="Payment">Whether it was a payment to a merchant (<see cref="LabelledExample.IsPayment"/>), the tie-breaker.</param>
/// <param name="Similarity">Cosine similarity of the embeddings.</param>
public sealed record EmbeddingNeighbour(string MerchantKey, Guid CategoryId, bool Payment, double Similarity);

/// <summary>Finds the user's labelled transactions nearest to an embedding: <c>pgvector</c> in the app, a list in the Eval tool.</summary>
public interface IEmbeddingNeighbours
{
    /// <param name="vector">The transaction's embedding.</param>
    /// <param name="outgoing">Only neighbours in the same direction of money.</param>
    /// <param name="count">At most this many, nearest first.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<EmbeddingNeighbour>> NearestAsync(float[] vector, bool outgoing, int count, CancellationToken ct);
}

/// <summary>Brute force over a list; a user has a few thousand labelled transactions at most.</summary>
public sealed class InMemoryNeighbours(IReadOnlyList<(LabelledExample Example, float[] Vector)> labelled) : IEmbeddingNeighbours
{
    public Task<IReadOnlyList<EmbeddingNeighbour>> NearestAsync(float[] vector, bool outgoing, int count, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EmbeddingNeighbour>>([.. labelled
            .Where(l => l.Example.Amount < 0 == outgoing)
            .Select(l => new EmbeddingNeighbour(l.Example.MerchantKey, l.Example.CategoryId, LabelledExample.IsPayment(l.Example.Kind), EmbeddingText.Cosine(vector, l.Vector)))
            .OrderByDescending(n => n.Similarity)
            .DistinctBy(n => (n.MerchantKey, n.CategoryId, n.Payment))
            .Take(count)]);
}

/// <summary>What the embedding model is given for a transaction and for an anchor.</summary>
public static class EmbeddingText
{
    /// <summary>e5 expects a prefix; <c>query:</c> on both sides for a symmetric comparison.</summary>
    public static string For(string merchantKey) => $"query: {merchantKey}";

    /// <summary>Anchors are folded like merchant keys, so both sides look alike.</summary>
    public static string ForAnchor(string text) => For(MerchantNormalizer.Fold(text));

    /// <summary>The vectors are unit length, so this is their dot product.</summary>
    public static double Cosine(float[] a, float[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}

/// <summary>
/// Tier C (plan 07): the embedding of the merchant key compared with the user's labelled
/// transactions (neighbours) and with the category anchors, for merchants nobody has labelled. The
/// best category is the one with the most similar neighbour or anchor; the confidence comes from
/// the margin over the second-best category.
/// </summary>
public sealed class EmbeddingClassifier(ITextEmbedder embedder, AnchorEmbeddings anchors, IEmbeddingNeighbours? neighbours) : IClassifier
{
    /// <summary>Labelled neighbours to look at.</summary>
    public const int K = 10;

    public async Task<IReadOnlyList<TierResult?>> ClassifyAsync(IReadOnlyList<RuleInput> batch, CancellationToken ct)
    {
        var results = new TierResult?[batch.Count];
        var todo = Enumerable.Range(0, batch.Count).Where(i => batch[i].Amount != 0 && batch[i].MerchantKey.Length > 0).ToList();
        if (todo.Count == 0)
        {
            return results;
        }

        var anchorVectors = await anchors.GetAsync(ct);
        // Merchant keys repeat a lot (a shop visited weekly): each one is embedded once.
        var keys = todo.Select(i => batch[i].MerchantKey).Distinct(StringComparer.Ordinal).ToList();
        var embedded = await embedder.EmbedAsync([.. keys.Select(EmbeddingText.For)], ct);
        var byKey = keys.Zip(embedded).ToDictionary(k => k.First, k => k.Second, StringComparer.Ordinal);
        for (var n = 0; n < todo.Count; n++)
        {
            var input = batch[todo[n]];
            var vector = byKey[input.MerchantKey];
            var outgoing = input.Amount < 0;
            var candidates = new List<(Guid CategoryId, double Similarity, string Reason, string Value)>();

            // The anchors describe spending categories, so they only apply to money going out.
            if (outgoing)
            {
                candidates.AddRange(anchorVectors.Select(a => (a.CategoryId, EmbeddingText.Cosine(vector, a.Vector), Reasons.CategoryAnchor, a.Text)));
            }

            if (neighbours is not null)
            {
                var near = await neighbours.NearestAsync(vector, outgoing, K, ct);
                var payment = LabelledExample.IsPayment(input.Kind);
                candidates.AddRange(near.Select(x =>
                    (x.CategoryId, x.Payment == payment ? x.Similarity : x.Similarity * MemoryClassifier.OtherKindWeight, Reasons.SimilarTo, x.MerchantKey)));
            }

            var perCategory = candidates
                .GroupBy(c => c.CategoryId)
                .Select(g => g.MaxBy(c => c.Similarity))
                .OrderByDescending(c => c.Similarity)
                .ToList();
            if (perCategory.Count == 0)
            {
                continue;
            }

            var best = perCategory[0];
            var margin = best.Similarity - (perCategory.Count > 1 ? perCategory[1].Similarity : 0);
            results[todo[n]] = new TierResult(best.CategoryId, ClassificationSource.Embedding, Calibrate(margin), best.Reason, best.Value);
        }

        return results;
    }

    /// <summary>
    /// e5 similarities are compressed (unrelated merchant strings still score ~0.85), so the margin
    /// over the second-best category is what carries the signal. Fitted on the fake local merchants
    /// (plan 07 *Results*): every pick with a margin above <see cref="MarginForReview"/> was right,
    /// below 0.02 most were wrong. Linear in the margin, reaching the review threshold at
    /// <see cref="MarginForReview"/>, capped below a rule's certainty.
    /// </summary>
    public static double Calibrate(double margin) =>
        Math.Round(Math.Clamp(margin / MarginForReview * Core.Classification.Confidence.ReviewBelow, 0, MaxConfidence), 4);

    /// <summary>The margin at which a guess reaches the review threshold.</summary>
    public const double MarginForReview = 0.03;

    /// <summary>An embedding is never as sure as a rule.</summary>
    public const double MaxConfidence = 0.95;
}

/// <summary>
/// The category anchors' embeddings, computed with the first batch and kept for the life of the
/// process: ~80 short texts, a few hundred kilobytes. They come from the embedded rules JSON, so a
/// changed JSON (a new deploy) is embedded afresh.
/// </summary>
public sealed class AnchorEmbeddings(ITextEmbedder embedder, IReadOnlyList<CategoryAnchor> anchors) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<(string Text, Guid CategoryId, float[] Vector)>? _vectors;

    public async Task<IReadOnlyList<(string Text, Guid CategoryId, float[] Vector)>> GetAsync(CancellationToken ct)
    {
        if (_vectors is { } ready)
        {
            return ready;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_vectors is null)
            {
                var texts = anchors.SelectMany(a => a.Texts.Select(t => (Text: t, a.CategoryId))).ToList();
                var vectors = await embedder.EmbedAsync([.. texts.Select(t => EmbeddingText.ForAnchor(t.Text))], ct);
                _vectors = [.. texts.Select((t, i) => (t.Text, t.CategoryId, vectors[i]))];
            }

            return _vectors;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();
}
