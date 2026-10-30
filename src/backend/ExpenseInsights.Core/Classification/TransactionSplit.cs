using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Core.Classification;

/// <summary>
/// One part of a transaction split across categories (e.g. a transfer that paid for dinner and a
/// ticket). The parts add up to the transaction's amount. The model only: the UI is in the backlog.
/// </summary>
public sealed class TransactionSplit : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid TransactionId { get; set; }

    public Guid CategoryId { get; set; }

    /// <summary>Signed like the transaction, in its currency.</summary>
    public decimal Amount { get; set; }

    public required string Currency { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
