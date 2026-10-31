using ExpenseInsights.Core.Classification;

namespace ExpenseInsights.Classification.Tiers;

/// <summary>
/// The tiers after the rules (plan 07), for what the rules leave unclassified or only guess. Memory
/// (A) first, because it's cheap; if it's confident, done. Otherwise the embeddings (C): when A and
/// C agree, the higher confidence; when they disagree, the more confident one if it reaches the
/// review threshold, otherwise the better guess goes to review. A confident rule always wins (D5).
/// </summary>
public sealed class TierPipeline(IClassifier? memory, IClassifier? embedding)
{
    /// <summary>A tier's guess below this is no guess at all: the transaction stays unclassified.</summary>
    public const double MinGuess = 0.4;

    /// <summary>The final classification per transaction, given what the rules found for each (best first).</summary>
    public async Task<IReadOnlyList<RuleMatch?>> ClassifyAsync(
        IReadOnlyList<RuleInput> inputs, IReadOnlyList<IReadOnlyList<RuleMatch>> ruleMatches, CancellationToken ct)
    {
        var results = ruleMatches.Select(m => m.Count > 0 ? m[0] : null).ToArray();
        var open = Enumerable.Range(0, inputs.Count).Where(i => IsOpen(results[i])).ToList();
        if (open.Count == 0 || (memory is null && embedding is null))
        {
            return results;
        }

        var fromMemory = memory is null ? new TierResult?[open.Count] : await memory.ClassifyAsync([.. open.Select(i => inputs[i])], ct);
        var stillOpen = Enumerable.Range(0, open.Count).Where(n => !IsConfident(fromMemory[n])).ToList();
        var fromEmbedding = new TierResult?[open.Count];
        if (embedding is not null && stillOpen.Count > 0)
        {
            var embedded = await embedding.ClassifyAsync([.. stillOpen.Select(n => inputs[open[n]])], ct);
            for (var j = 0; j < stillOpen.Count; j++)
            {
                fromEmbedding[stillOpen[j]] = embedded[j];
            }
        }

        for (var n = 0; n < open.Count; n++)
        {
            var i = open[n];
            results[i] = Combine(results[i], fromMemory[n], fromEmbedding[n]);
        }

        return results;
    }

    /// <summary>The combining rule for one transaction: the rules' guess (if any), then A, then C.</summary>
    public static RuleMatch? Combine(RuleMatch? rule, TierResult? a, TierResult? c)
    {
        if (IsConfident(a))
        {
            return a!.ToMatch();
        }

        TierResult? tier;
        if (a is not null && c is not null && a.CategoryId == c.CategoryId)
        {
            tier = a.Confidence >= c.Confidence ? a : c;
        }
        else
        {
            tier = new[] { a, c }.Where(t => t is not null).MaxBy(t => t!.Confidence);
        }

        if (tier is null || tier.Confidence < MinGuess)
        {
            return rule;
        }

        // A guess of the rules stays unless a tier is sure, or has a better guess.
        return rule is null || IsConfident(tier) || tier.Confidence > rule.Confidence ? tier.ToMatch() : rule;
    }

    /// <summary>Unclassified, or a guess for review: what the tiers are for.</summary>
    private static bool IsOpen(RuleMatch? match) => match is null || Confidence.NeedsReview(match.Source, match.Confidence);

    private static bool IsConfident(TierResult? result) => result is not null && result.Confidence >= Confidence.ReviewBelow;
}
