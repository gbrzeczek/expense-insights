namespace ExpenseInsights.Core.Classification;

public static class Confidence
{
    /// <summary>A classification below this goes to the "Do sprawdzenia" (needs review) queue.</summary>
    public const double ReviewBelow = 0.7;

    public static bool NeedsReview(ClassificationSource source, double confidence) =>
        source is ClassificationSource.GlobalRule or ClassificationSource.UserRule && confidence < ReviewBelow;
}
