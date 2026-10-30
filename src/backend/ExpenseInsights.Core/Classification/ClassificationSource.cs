namespace ExpenseInsights.Core.Classification;

/// <summary>
/// Where a transaction's category came from. Only a <see cref="UserCorrection"/> is never
/// recomputed; everything else is re-derived when the rules change.
/// </summary>
public enum ClassificationSource
{
    Unclassified,
    GlobalRule,
    UserRule,
    UserCorrection,
}
