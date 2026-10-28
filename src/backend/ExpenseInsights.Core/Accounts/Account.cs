using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Core.Accounts;

/// <summary>A bank account of the user, created from a statement's account list.</summary>
public sealed class Account : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string BankId { get; set; }

    /// <summary>The bank's label for the account; statement rows refer to it by this name.</summary>
    public required string Name { get; set; }

    /// <summary>For display only, e.g. <c>•••• 3863</c>. The full number is never stored.</summary>
    public required string NumberMasked { get; set; }

    /// <summary>SHA-256 of the normalized number: identifies the account and own transfers.</summary>
    public required string NumberHash { get; set; }

    public required string Currency { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
