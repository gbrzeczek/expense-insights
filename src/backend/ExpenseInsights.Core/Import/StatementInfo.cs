namespace ExpenseInsights.Core.Import;

/// <summary>What a statement says about itself. Holds no personal data of the account holder.</summary>
public sealed record StatementInfo
{
    public required string BankId { get; init; }

    public string? DocumentId { get; init; }

    public DateTimeOffset? GeneratedAt { get; init; }

    public DateOnly? PeriodStart { get; init; }

    public DateOnly? PeriodEnd { get; init; }

    /// <summary>The accounts the statement covers; rows link to them by <see cref="StatementAccount.Ref"/>.</summary>
    public IReadOnlyList<StatementAccount> Accounts { get; init; } = [];

    /// <summary>Declared credit totals (count and sum), for an integrity check.</summary>
    public DeclaredTotal? Credits { get; init; }

    /// <summary>Declared debit totals; the sum is a positive magnitude.</summary>
    public DeclaredTotal? Debits { get; init; }
}

/// <summary>
/// An account listed by the statement. Rows refer to it by <c>Ref</c> (see
/// <see cref="ImportedTransaction.AccountRef"/>). <c>Number</c> is the full account number: never
/// log it, and persist only a mask and a hash.
/// </summary>
public sealed record StatementAccount(string Ref, string Number, string Currency);

public sealed record DeclaredTotal(int Count, decimal Sum, string Currency);
