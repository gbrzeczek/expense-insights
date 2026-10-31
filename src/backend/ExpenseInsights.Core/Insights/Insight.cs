namespace ExpenseInsights.Core.Insights;

public enum InsightType
{
    /// <summary>A category's month is unusual against its baseline (and its season).</summary>
    CategoryChange,

    /// <summary>The month's spending, projected from the typical day-of-month profile, is unusual.</summary>
    Pace,

    /// <summary>One payment far above what the category usually sees.</summary>
    LargePayment,

    /// <summary>More small payments at one merchant than usual (count and sum).</summary>
    FrequentSmall,

    NewSubscription,

    SubscriptionPriceChange,

    /// <summary>A reminder: money out this month still without a category.</summary>
    Uncategorized,
}

public enum InsightDirection
{
    Up,
    Down,
}

/// <summary>
/// One observation, as typed parameters: the UI words it through i18n templates, and every number in
/// it is computed here (hard rule 6). Which fields are set depends on <see cref="Type"/>.
/// </summary>
/// <remarks>
/// <para><c>Score</c>: The ranking score (<see cref="InsightStatistics.Score"/>); higher is more notable.</para>
/// <para><c>CategoryId</c>: The top-level category it is about; <c>null</c> for the whole month or a merchant.</para>
/// <para><c>TransactionId</c>: The payment, for <see cref="InsightType.LargePayment"/>.</para>
/// <para><c>Amount</c>: This month's amount: the category total, the projected month (pace), the payment, the merchant's sum, the subscription's (new) price, or what is unclassified.</para>
/// <para><c>Usual</c>: What it is compared with: the baseline median, the category's median payment, or the old price.</para>
/// <para><c>Change</c>: (amount − usual) ÷ usual; <c>null</c> when the usual amount is zero.</para>
/// <para><c>Months</c>: How many months the baseline is the median of.</para>
/// <para><c>LastYear</c>: The seasonal reference it was also unusual against, with 13+ months of history.</para>
/// <para><c>SoFar</c>: For pace: spent so far this month.</para>
/// <para><c>Count</c>: Payments this month (frequent small, uncategorized).</para>
/// <para><c>UsualCount</c>: The median number of payments a month (frequent small).</para>
/// <para><c>CadenceDays</c>: For subscriptions: 7, 30 or 365.</para>
/// </remarks>
public sealed record Insight(
    InsightType Type,
    double Score,
    InsightDirection Direction = InsightDirection.Up,
    Guid? CategoryId = null,
    string? MerchantKey = null,
    string? Merchant = null,
    Guid? TransactionId = null,
    decimal? Amount = null,
    decimal? Usual = null,
    int? Months = null,
    decimal? LastYear = null,
    decimal? SoFar = null,
    int? Count = null,
    int? UsualCount = null,
    int? CadenceDays = null)
{
    public decimal? Change => Amount is { } amount && Usual is { } usual && usual != 0 ? Math.Round((amount - usual) / usual, 4) : null;
}
