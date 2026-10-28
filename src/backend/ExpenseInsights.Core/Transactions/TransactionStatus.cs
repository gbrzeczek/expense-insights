namespace ExpenseInsights.Core.Transactions;

public enum TransactionStatus
{
    /// <summary>A hold (blokada): no booking date or bank id yet.</summary>
    Pending,
    Booked,

    /// <summary>A pending row that never got booked. Kept, never deleted.</summary>
    Expired,
}
