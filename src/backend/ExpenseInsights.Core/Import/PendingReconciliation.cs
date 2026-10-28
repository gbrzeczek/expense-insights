using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Core.Import;

/// <summary>
/// Finds the pending hold a booked row replaces: same account and amount, booked 0–5 days after
/// the hold's date, and a similar counterparty.
/// </summary>
public static class PendingReconciliation
{
    public const int MaxDaysToBooking = 5;

    public static Transaction? FindPending(ImportedTransaction booked, IEnumerable<Transaction> pending) =>
        pending
            .Select(p => (Pending: p, Days: (booked.PurchaseDate ?? booked.TransactionDate).DayNumber - p.MatchDate.DayNumber))
            .Where(c => c.Pending.Amount == booked.Amount && c.Days is >= 0 and <= MaxDaysToBooking)
            .Select(c => (c.Pending, c.Days, Similarity: TrigramSimilarity.Similarity(c.Pending.CounterpartyName, booked.CounterpartyName)))
            .Where(c => c.Similarity >= TrigramSimilarity.DefaultThreshold)
            .OrderBy(c => c.Days)
            .ThenByDescending(c => c.Similarity)
            .Select(c => c.Pending)
            .FirstOrDefault();

    /// <summary>Whether a pending row in a new file is the same hold as one already stored.</summary>
    public static bool IsSameHold(ImportedTransaction incoming, Transaction stored) =>
        stored.Amount == incoming.Amount
        && stored.MatchDate == (incoming.PurchaseDate ?? incoming.TransactionDate)
        && TrigramSimilarity.Similarity(stored.CounterpartyName, incoming.CounterpartyName) >= TrigramSimilarity.DefaultThreshold;
}
