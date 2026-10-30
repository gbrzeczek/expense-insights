namespace ExpenseInsights.Core.Classification;

/// <summary>How a category counts in summaries. Subcategories inherit it from their parent.</summary>
public enum CategoryKind
{
    Spending,
    Income,

    /// <summary>Money back for a purchase: offsets spending instead of counting as income.</summary>
    Refund,

    /// <summary>Moves between the user's own accounts: neither spending nor income.</summary>
    Excluded,
}
