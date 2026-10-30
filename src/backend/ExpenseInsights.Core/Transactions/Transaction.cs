using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Core.Transactions;

public sealed class Transaction : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>The import that first brought the transaction in.</summary>
    public Guid ImportBatchId { get; set; }

    /// <summary>The bank's id; <c>null</c> while pending. Unique per account.</summary>
    public string? ExternalId { get; set; }

    public TransactionStatus Status { get; set; }

    public DateOnly TransactionDate { get; set; }

    public DateOnly? BookingDate { get; set; }

    public DateOnly? PurchaseDate { get; set; }

    public decimal Amount { get; set; }

    public required string Currency { get; set; }

    public decimal? OriginalAmount { get; set; }

    public string? OriginalCurrency { get; set; }

    public TransactionKind Kind { get; set; }

    public required string CounterpartyRaw { get; set; }

    public required string TitleRaw { get; set; }

    public required string CounterpartyName { get; set; }

    public string? MerchantHint { get; set; }

    public string? CounterpartyAccount { get; set; }

    public string? CounterpartyBank { get; set; }

    public decimal? BalanceAfter { get; set; }

    /// <summary>The other leg of an own transfer (on the user's other account).</summary>
    public Guid? OwnTransferPairId { get; set; }

    public required RawRow RawRow { get; set; }

    /// <summary>The normalized merchant (<c>biedronka</c>): groups transactions and matches rules.</summary>
    public string MerchantKey { get; set; } = "";

    /// <summary>The current category; <c>null</c> while unclassified.</summary>
    public Guid? CategoryId { get; set; }

    public ClassificationSource ClassificationSource { get; set; }

    /// <summary>0–1. Below <see cref="Classification.Confidence.ReviewBelow"/> it goes to "Do sprawdzenia".</summary>
    public double ClassificationConfidence { get; set; }

    /// <summary>The user rule that set the category, if one did.</summary>
    public Guid? ClassificationRuleId { get; set; }

    /// <summary>Why it got the category, as an i18n code (<c>merchantExact</c>), plus its value (the pattern).</summary>
    public string? ClassificationReason { get; set; }

    public string? ClassificationReasonValue { get; set; }

    public DateTimeOffset? ClassifiedAt { get; set; }

    /// <summary>
    /// For incoming money: the spending category it pays back (a friend returning their share of the
    /// shopping offsets groceries instead of counting as income).
    /// </summary>
    public Guid? OffsetsCategoryId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The date used to match a pending hold with its booked row.</summary>
    public DateOnly MatchDate => PurchaseDate ?? TransactionDate;

    /// <summary>Overwrites the bank-provided fields; keeps the id and anything the user set.</summary>
    public void ApplyImported(ImportedTransaction imported)
    {
        ExternalId = imported.ExternalId;
        Status = imported.Status;
        TransactionDate = imported.TransactionDate;
        BookingDate = imported.BookingDate;
        PurchaseDate = imported.PurchaseDate;
        Amount = imported.Amount;
        Currency = imported.Currency;
        OriginalAmount = imported.OriginalAmount;
        OriginalCurrency = imported.OriginalCurrency;
        Kind = imported.Kind;
        CounterpartyRaw = imported.CounterpartyRaw;
        TitleRaw = imported.TitleRaw;
        CounterpartyName = imported.CounterpartyName;
        MerchantHint = imported.MerchantHint;
        CounterpartyAccount = imported.CounterpartyAccount;
        CounterpartyBank = imported.CounterpartyBank;
        BalanceAfter = imported.BalanceAfter;
        RawRow = imported.RawRow;
    }
}
