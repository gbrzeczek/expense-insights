using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Core.Import;

/// <summary>The bank-agnostic transaction model everything downstream of a parser uses.</summary>
public sealed record ImportedTransaction
{
    public required string BankId { get; init; }

    /// <summary>The account's label in the statement; see <see cref="StatementAccount.Ref"/>.</summary>
    public required string AccountRef { get; init; }

    /// <summary>The bank's transaction id; <c>null</c> for pending rows.</summary>
    public string? ExternalId { get; init; }

    public required TransactionStatus Status { get; init; }

    public required DateOnly TransactionDate { get; init; }

    public DateOnly? BookingDate { get; init; }

    /// <summary>For card payments, the date from the title.</summary>
    public DateOnly? PurchaseDate { get; init; }

    /// <summary>Signed, in the account currency: expenses negative, income positive.</summary>
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public decimal? OriginalAmount { get; init; }

    public string? OriginalCurrency { get; init; }

    public required TransactionKind Kind { get; init; }

    public required string CounterpartyRaw { get; init; }

    public required string TitleRaw { get; init; }

    /// <summary>The counterparty trimmed, with whitespace runs collapsed.</summary>
    public required string CounterpartyName { get; init; }

    /// <summary>For payments through a processor (BLIK via PayU etc.): the shop's domain.</summary>
    public string? MerchantHint { get; init; }

    public string? CounterpartyAccount { get; init; }

    public string? CounterpartyBank { get; init; }

    public decimal? BalanceAfter { get; init; }

    public required RawRow RawRow { get; init; }
}

/// <summary>The original columns of the row, so it can be re-parsed when the parser improves.</summary>
public sealed record RawRow(int LineNumber, IReadOnlyList<string> Fields);
