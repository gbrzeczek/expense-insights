using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Core.Insights;

/// <summary>A transaction as the ledger needs it (one query, no tracking).</summary>
/// <remarks>
/// <para><c>Amount</c>: Signed like the bank: expenses negative.</para>
/// <para><c>Merchant</c>: For display: the shop's domain or the counterparty.</para>
/// <para><c>Splits</c>: Parts in other categories; when present they replace the transaction's own category.</para>
/// </remarks>
public sealed record LedgerTransaction(
    Guid Id,
    DateOnly Date,
    decimal Amount,
    string Currency,
    TransactionKind Kind,
    TransactionStatus Status,
    Guid? CategoryId,
    Guid? OffsetsCategoryId,
    string MerchantKey,
    string Merchant,
    IReadOnlyList<LedgerSplit> Splits);

/// <remarks>
/// <para><c>Amount</c>: Signed like the transaction.</para>
/// </remarks>
public sealed record LedgerSplit(Guid CategoryId, decimal Amount);

public enum Flow
{
    Spending,
    Income,
}

/// <summary>
/// What a transaction (or one of its splits) counts as in summaries.
/// </summary>
/// <remarks>
/// <para><c>CategoryId</c>: The top-level category; <c>null</c> while unclassified.</para>
/// <para><c>Amount</c>: Positive when it adds to the flow (an expense to spending, a salary to income), negative when it takes away (a refund or a reimbursement offsets its spending category).</para>
/// </remarks>
public sealed record LedgerEntry(Guid TransactionId, DateOnly Date, Flow Flow, Guid? CategoryId, decimal Amount, string MerchantKey, string Merchant)
{
    public Month Month => Month.Of(Date);
}

/// <summary>
/// Turns transactions into spending and income (plan 06, *Aggregation*): subcategories roll up to
/// their top-level category; own transfers and expired holds don't count; a refund offsets the
/// category its merchant is usually in (or the one the user picked), and a reimbursement offsets the
/// category it pays back (D11). Unclassified money out is spending without a category.
/// </summary>
public static class SpendingLedger
{
    public static IReadOnlyList<LedgerEntry> Build(IEnumerable<LedgerTransaction> transactions, IReadOnlyCollection<Category> categories, string currency)
    {
        var byId = categories.ToDictionary(c => c.Id);
        var rows = transactions.Where(t => t.Currency == currency && t.Status != TransactionStatus.Expired && t.Kind != TransactionKind.OwnTransfer).ToList();

        Guid? Top(Guid? id)
        {
            while (id is { } current && byId.TryGetValue(current, out var category) && category.ParentId is { } parent)
            {
                id = parent;
            }

            return id is { } known && byId.ContainsKey(known) ? known : null;
        }

        CategoryKind? KindOf(Guid? id) => Top(id) is { } top ? byId[top].Kind : null;

        // Where a merchant's refunds go: the spending category most of its purchases are in.
        var usualCategory = rows
            .Where(t => t.Amount < 0 && KindOf(t.CategoryId) == CategoryKind.Spending && t.MerchantKey.Length > 0)
            .GroupBy(t => t.MerchantKey)
            .ToDictionary(g => g.Key, g => g.GroupBy(t => Top(t.CategoryId)!.Value).OrderByDescending(c => c.Count()).ThenBy(c => c.Key).First().Key);

        var entries = new List<LedgerEntry>();
        foreach (var t in rows)
        {
            IEnumerable<(Guid? CategoryId, decimal Amount)> parts = t.Splits.Count > 0
                ? t.Splits.Select(s => ((Guid?)s.CategoryId, s.Amount))
                : [(t.CategoryId, t.Amount)];
            foreach (var (categoryId, amount) in parts)
            {
                LedgerEntry Entry(Flow flow, Guid? category, decimal value) => new(t.Id, t.Date, flow, category, value, t.MerchantKey, t.Merchant);

                switch (KindOf(categoryId))
                {
                    case null when amount < 0:
                        entries.Add(Entry(Flow.Spending, null, -amount));
                        break;
                    case null:
                        entries.Add(Entry(Flow.Income, null, amount));
                        break;
                    case CategoryKind.Spending:
                        entries.Add(Entry(Flow.Spending, Top(categoryId), -amount));
                        break;
                    case CategoryKind.Income when KindOf(t.OffsetsCategoryId) == CategoryKind.Spending:
                        entries.Add(Entry(Flow.Spending, Top(t.OffsetsCategoryId), -amount));
                        break;
                    case CategoryKind.Income:
                        entries.Add(Entry(Flow.Income, Top(categoryId), amount));
                        break;
                    case CategoryKind.Refund:
                        var target = KindOf(t.OffsetsCategoryId) == CategoryKind.Spending
                            ? Top(t.OffsetsCategoryId)!.Value
                            : usualCategory.GetValueOrDefault(t.MerchantKey, CategoryCatalog.Other.Id);
                        entries.Add(Entry(Flow.Spending, target, -amount));
                        break;
                    case CategoryKind.Excluded:
                        break;
                }
            }
        }

        return entries;
    }
}
