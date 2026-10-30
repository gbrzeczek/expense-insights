using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Core.Classification;

/// <summary>Every change of a transaction's category: an audit trail and, later, training data.</summary>
public sealed class ClassificationHistory : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid TransactionId { get; set; }

    public Guid? PreviousCategoryId { get; set; }

    public Guid? CategoryId { get; set; }

    public ClassificationSource Source { get; set; }

    public double Confidence { get; set; }

    public Guid? RuleId { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
