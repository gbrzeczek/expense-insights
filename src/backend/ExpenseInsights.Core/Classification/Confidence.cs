namespace ExpenseInsights.Core.Classification;

public static class Confidence
{
    /// <summary>A classification below this goes to the "Do sprawdzenia" (needs review) queue.</summary>
    public const double ReviewBelow = 0.7;

    /// <summary>Whether the category is a guess to review: anything computed (not the user's own choice) below <see cref="ReviewBelow"/>.</summary>
    public static bool NeedsReview(ClassificationSource source, double confidence) =>
        IsComputed(source) && confidence < ReviewBelow;

    /// <summary>A category the pipeline computed, which a rule or label change may recompute.</summary>
    public static bool IsComputed(ClassificationSource source) =>
        source is not (ClassificationSource.Unclassified or ClassificationSource.UserCorrection);
}
