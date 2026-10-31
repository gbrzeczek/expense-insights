using ExpenseInsights.Core.Classification;

namespace ExpenseInsights.Classification.Tiers;

/// <summary>
/// Tier A (plan 07): k nearest neighbours over character n-gram TF-IDF of the merchant keys the
/// user has labelled. No training: every correction is another example, in effect at once. Pure
/// and in memory; a user has a few thousand labelled rows at most.
/// </summary>
public sealed class MemoryClassifier : IClassifier
{
    /// <summary>Neighbours that vote.</summary>
    public const int K = 5;

    /// <summary>Cosine similarity below which a labelled merchant doesn't count as a neighbour.</summary>
    public const double MinSimilarity = 0.3;

    /// <summary>A neighbour of another kind (a card payment vs a transfer) counts this much: same name, maybe not the same payee.</summary>
    public const double OtherKindWeight = 0.6;

    private readonly Memory[] _memories;

    public MemoryClassifier(IEnumerable<LabelledExample> examples)
    {
        var byDirection = examples
            .Where(e => e.Amount != 0 && e.MerchantKey.Length > 0)
            .GroupBy(e => e.Amount < 0);
        _memories = [.. byDirection.Select(g => new Memory(g.Key, g))];
    }

    /// <summary>Whether there is anything to compare with.</summary>
    public bool IsEmpty => _memories.Length == 0;

    public Task<IReadOnlyList<TierResult?>> ClassifyAsync(IReadOnlyList<RuleInput> batch, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TierResult?>>([.. batch.Select(Classify)]);

    public TierResult? Classify(RuleInput input)
    {
        // Only examples in the same direction: money in from a shop is a refund, not groceries.
        var memory = _memories.FirstOrDefault(m => m.Outgoing == input.Amount < 0);
        if (memory is null || input.Amount == 0 || input.MerchantKey.Length == 0)
        {
            return null;
        }

        var payment = LabelledExample.IsPayment(input.Kind);
        var neighbours = memory.Nearest(input.MerchantKey)
            .Select(n => (n.Doc, Similarity: n.Doc.Payment == payment ? n.Similarity : n.Similarity * OtherKindWeight))
            .Where(n => n.Similarity >= MinSimilarity)
            .ToList();
        if (neighbours.Count == 0)
        {
            return null;
        }

        var votes = new Dictionary<Guid, double>();
        foreach (var (doc, similarity) in neighbours)
        {
            foreach (var (category, share) in doc.Categories)
            {
                votes[category] = votes.GetValueOrDefault(category) + similarity * share;
            }
        }

        var (winner, vote) = votes.MaxBy(v => v.Value);
        var (nearest, nearestSimilarity) = neighbours.Where(n => n.Doc.Categories.ContainsKey(winner)).MaxBy(n => n.Similarity);
        var voteShare = vote / votes.Values.Sum();
        return new TierResult(winner, ClassificationSource.Similarity, Calibrate(voteShare, nearestSimilarity), Reasons.SimilarTo, nearest.MerchantKey);
    }

    /// <summary>
    /// The vote share times the nearest neighbour's similarity, which on the fake labels already
    /// tracks precision (see the reliability table of the Eval tool, plan 07 *Results*).
    /// </summary>
    public static double Calibrate(double voteShare, double similarity) => Math.Round(voteShare * similarity, 4);

    /// <summary>A labelled merchant key with how often each category was given to it.</summary>
    private sealed record Doc(string MerchantKey, bool Payment, IReadOnlyDictionary<Guid, double> Categories);

    /// <summary>The labelled merchants for one direction of money, indexed by n-gram.</summary>
    private sealed class Memory
    {
        private readonly Doc[] _docs;
        private readonly Dictionary<string, double> _idf;
        private readonly double _unseenIdf;
        private readonly Dictionary<string, List<(int Doc, double Weight)>> _index = new(StringComparer.Ordinal);

        public Memory(bool outgoing, IEnumerable<LabelledExample> examples)
        {
            Outgoing = outgoing;
            _docs = [.. examples
                .GroupBy(e => (e.MerchantKey, Payment: LabelledExample.IsPayment(e.Kind)))
                .Select(g =>
                {
                    var total = (double)g.Count();
                    return new Doc(g.Key.MerchantKey, g.Key.Payment, g.GroupBy(e => e.CategoryId).ToDictionary(c => c.Key, c => c.Count() / total));
                })];

            var grams = _docs.Select(d => CharNGrams.Count(d.MerchantKey)).ToArray();
            var df = grams.SelectMany(g => g.Keys).GroupBy(g => g, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            _idf = df.ToDictionary(d => d.Key, d => Idf(_docs.Length, d.Value), StringComparer.Ordinal);
            _unseenIdf = Idf(_docs.Length, 0);
            for (var i = 0; i < _docs.Length; i++)
            {
                foreach (var (gram, weight) in Vector(grams[i]))
                {
                    if (!_index.TryGetValue(gram, out var postings))
                    {
                        _index[gram] = postings = [];
                    }

                    postings.Add((i, weight));
                }
            }
        }

        public bool Outgoing { get; }

        public List<(Doc Doc, double Similarity)> Nearest(string merchantKey)
        {
            var scores = new Dictionary<int, double>();
            foreach (var (gram, weight) in Vector(CharNGrams.Count(merchantKey)))
            {
                if (_index.TryGetValue(gram, out var postings))
                {
                    foreach (var (doc, docWeight) in postings)
                    {
                        scores[doc] = scores.GetValueOrDefault(doc) + weight * docWeight;
                    }
                }
            }

            return [.. scores
                .Where(s => s.Value >= MinSimilarity)
                .OrderByDescending(s => s.Value)
                .ThenBy(s => _docs[s.Key].MerchantKey, StringComparer.Ordinal)
                .Take(K)
                .Select(s => (_docs[s.Key], Math.Min(1, s.Value)))];
        }

        /// <summary>Sublinear TF × IDF, L2-normalized. An n-gram no labelled merchant has still counts in the norm.</summary>
        private Dictionary<string, double> Vector(Dictionary<string, int> counts)
        {
            var vector = counts.ToDictionary(c => c.Key, c => (1 + Math.Log(c.Value)) * _idf.GetValueOrDefault(c.Key, _unseenIdf), StringComparer.Ordinal);
            var norm = Math.Sqrt(vector.Values.Sum(v => v * v));
            foreach (var key in vector.Keys)
            {
                vector[key] /= norm;
            }

            return vector;
        }

        /// <summary>Smoothed, as scikit-learn's <c>TfidfVectorizer</c> does.</summary>
        private static double Idf(int docs, int df) => Math.Log((1.0 + docs) / (1.0 + df)) + 1;
    }
}

/// <summary>Character n-grams of a merchant key, the features of tier A.</summary>
public static class CharNGrams
{
    public const int Min = 3;
    public const int Max = 4;

    /// <summary>
    /// The 3- and 4-grams of the key padded with a space at both ends, so word starts and ends are
    /// features too. Robust to inflection, truncation and missing diacritics (the key is folded).
    /// </summary>
    public static Dictionary<string, int> Count(string merchantKey)
    {
        var padded = $" {merchantKey} ";
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var n = Min; n <= Max; n++)
        {
            for (var i = 0; i + n <= padded.Length; i++)
            {
                var gram = padded.Substring(i, n);
                counts[gram] = counts.GetValueOrDefault(gram) + 1;
            }
        }

        return counts;
    }
}
