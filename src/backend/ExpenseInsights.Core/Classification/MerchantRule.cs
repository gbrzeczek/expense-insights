using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Core.Classification;

/// <summary>
/// A user's rule, usually created by "apply to all" after a correction. The global rules aren't
/// stored: they ship with the app (<c>global-rules.json</c>) and apply to everyone.
/// </summary>
public sealed class MerchantRule : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public RuleMatchType MatchType { get; set; }

    /// <summary>A merchant key, a title keyword, or for <see cref="RuleMatchType.CounterpartyAccount"/> an account hash.</summary>
    public required string Pattern { get; set; }

    public Guid CategoryId { get; set; }

    /// <summary>Higher wins among the user's rules.</summary>
    public int Priority { get; set; }

    /// <summary>What the rule was made from, for display (<c>Biedronka</c>, <c>Anna Nowak</c>). Never an account number.</summary>
    public string? Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
