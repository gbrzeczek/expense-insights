namespace ExpenseInsights.Core.Insights;

/// <summary>
/// Runs the insight rules for a month and ranks what they found: one insight per subject (the
/// highest scoring), the top <see cref="MaxRanked"/> by score, then the uncategorized reminder.
/// When nothing clears its threshold the list is empty, and the UI says so.
/// </summary>
public sealed class InsightEngine(IReadOnlyList<IInsightRule> rules)
{
    public const int MaxRanked = 3;

    public static InsightEngine Default { get; } = new(
    [
        new CategoryChangeRule(),
        new PaceRule(),
        new LargePaymentRule(),
        new FrequentSmallRule(),
        new SubscriptionRule(),
        new UncategorizedRule(),
    ]);

    public IReadOnlyList<Insight> Evaluate(InsightContext context)
    {
        var found = rules.SelectMany(r => r.Evaluate(context)).ToList();
        var ranked = found
            .Where(i => i.Type != InsightType.Uncategorized)
            .GroupBy(Subject)
            .Select(g => g.OrderByDescending(i => i.Score).First())
            .OrderByDescending(i => i.Score)
            .ThenBy(i => i.Type)
            .Take(MaxRanked);
        return [.. ranked, .. found.Where(i => i.Type == InsightType.Uncategorized).Take(1)];
    }

    /// <summary>
    /// What an insight is about. A category's spike and its largest payment tell one story, so only
    /// the stronger is shown; a merchant's story (a subscription, many small payments) is separate.
    /// </summary>
    private static string Subject(Insight insight) => insight.Type switch
    {
        InsightType.CategoryChange or InsightType.LargePayment => $"category:{insight.CategoryId}",
        InsightType.Pace => "month",
        _ => $"merchant:{insight.MerchantKey}",
    };
}
