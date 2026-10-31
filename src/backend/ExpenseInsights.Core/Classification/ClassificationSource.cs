namespace ExpenseInsights.Core.Classification;

/// <summary>
/// Where a transaction's category came from. Only a <see cref="UserCorrection"/> is never
/// recomputed; everything else is re-derived when the rules or the user's labels change.
/// </summary>
public enum ClassificationSource
{
    Unclassified,
    GlobalRule,
    UserRule,
    UserCorrection,

    /// <summary>Tier A (plan 07): the nearest of the user's labelled transactions by merchant name.</summary>
    Similarity,

    /// <summary>Tier C (plan 07): the local embedding model, against labelled transactions and category anchors.</summary>
    Embedding,
}
