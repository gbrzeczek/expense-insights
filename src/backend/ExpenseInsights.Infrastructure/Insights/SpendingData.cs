using ExpenseInsights.Core.Insights;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Insights;

/// <summary>The current user's spending history in their main currency.</summary>
/// <remarks>
/// <para><c>EarliestMonth</c>: The month of the first transaction (it may be only partly imported).</para>
/// </remarks>
public sealed record UserSpending(SpendingHistory History, string Currency, Month EarliestMonth);

/// <summary>
/// Loads the current user's transactions (the query filter scopes every query, hard rule 7) into
/// the spending ledger. A few thousand rows a user: read once per request and aggregated in memory,
/// which keeps the SQL boring.
/// </summary>
public sealed class SpendingData(AppDbContext db)
{
    public const string DefaultCurrency = "PLN";

    /// <summary><c>null</c> when the user has no transactions yet.</summary>
    public async Task<UserSpending?> LoadAsync(CancellationToken ct)
    {
        var rows = await db.Transactions.AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.TransactionDate,
                t.Amount,
                t.Currency,
                t.Kind,
                t.Status,
                t.CategoryId,
                t.OffsetsCategoryId,
                t.MerchantKey,
                Merchant = t.MerchantHint ?? t.CounterpartyName,
            })
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return null;
        }

        // Summaries are in one currency: the one most transactions are in (ING: PLN).
        var currency = rows.GroupBy(r => r.Currency).OrderByDescending(g => g.Count()).ThenBy(g => g.Key != DefaultCurrency).First().Key;
        var splits = (await db.TransactionSplits.AsNoTracking().Select(s => new { s.TransactionId, s.CategoryId, s.Amount }).ToListAsync(ct))
            .ToLookup(s => s.TransactionId, s => new LedgerSplit(s.CategoryId, s.Amount));
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var periods = await db.ImportBatches.AsNoTracking()
            .Where(b => b.PeriodStart != null && b.PeriodEnd != null)
            .Select(b => new { Start = b.PeriodStart!.Value, End = b.PeriodEnd!.Value })
            .ToListAsync(ct);

        var transactions = rows.Select(r => new LedgerTransaction(
            r.Id, r.TransactionDate, r.Amount, r.Currency, r.Kind, r.Status, r.CategoryId, r.OffsetsCategoryId, r.MerchantKey, r.Merchant, [.. splits[r.Id]]));
        var first = rows.Min(r => r.TransactionDate);
        var last = rows.Max(r => r.TransactionDate);

        // What the statements say they cover; the transactions themselves when they don't say.
        var start = periods.Count > 0 ? periods.Min(p => p.Start) : first;
        var end = periods.Count > 0 ? new[] { periods.Max(p => p.End), last }.Max() : last;
        var history = new SpendingHistory(SpendingLedger.Build(transactions, categories, currency), SpendingHistory.FirstFullMonth(start), end);
        return new UserSpending(history, currency, Month.Of(new[] { start, first }.Min()));
    }
}
