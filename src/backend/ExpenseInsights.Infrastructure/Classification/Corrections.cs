using ExpenseInsights.Classification;
using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Classification;

/// <summary>How "apply to all" would generalize a correction: by merchant, or by the person's account.</summary>
/// <param name="MatchType"><see cref="RuleMatchType.Exact"/> (merchant key) or <see cref="RuleMatchType.CounterpartyAccount"/>.</param>
/// <param name="Label">The merchant key or the person's name, for display.</param>
/// <param name="Count">Other transactions it would recategorize.</param>
public sealed record ApplyToAllOffer(RuleMatchType MatchType, string Label, int Count);

public sealed class CorrectionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The user's corrections: one transaction, everything from a merchant ("apply to all"), and
/// rules. Each turns into a <see cref="MerchantRule"/> where it generalizes, so future imports follow.
/// </summary>
/// <param name="db">The current user's data.</param>
/// <param name="classifier">Reclassifies what a rule change affects.</param>
/// <param name="clock">For timestamps.</param>
/// <param name="requests">After a correction, the user's open transactions are reclassified in the background: the memory tier learns from it at once (plan 07).</param>
public sealed class Corrections(AppDbContext db, TransactionClassifier classifier, TimeProvider clock, ClassificationRequests? requests = null)
{
    public const string TransactionNotFound = "transactionNotFound";
    public const string CategoryNotFound = "categoryNotFound";
    public const string RuleNotFound = "ruleNotFound";
    public const string NotIncoming = "notIncoming";
    public const string NotSpending = "notSpending";
    public const string NothingToApply = "nothingToApply";

    /// <summary>Sets the category of one transaction. It is never recomputed after this.</summary>
    public async Task<Transaction> CorrectAsync(Guid transactionId, Guid categoryId, CancellationToken ct)
    {
        var transaction = await FindAsync(transactionId, ct);
        await RequireCategoryAsync(categoryId, ct);

        var now = clock.GetUtcNow();
        var previous = transaction.CategoryId;
        transaction.CategoryId = categoryId;
        transaction.ClassificationSource = ClassificationSource.UserCorrection;
        transaction.ClassificationConfidence = 1;
        transaction.ClassificationRuleId = null;
        transaction.ClassificationReason = null;
        transaction.ClassificationReasonValue = null;
        transaction.ClassifiedAt = now;
        transaction.UpdatedAt = now;
        classifier.Record(transaction, previous, now);
        await db.SaveChangesAsync(ct);
        requests?.Enqueue(db.CurrentUserId);
        return transaction;
    }

    /// <summary>What "apply to all" would do for this transaction, or <c>null</c> when there is nothing to apply.</summary>
    public async Task<ApplyToAllOffer?> OfferAsync(Transaction transaction, CancellationToken ct)
    {
        if (transaction.CategoryId is not { } categoryId || Generalize(transaction) is not var (matchType, pattern, label))
        {
            return null;
        }

        var count = await Matching(matchType, pattern)
            .Where(t => t.Id != transaction.Id && t.ClassificationSource != ClassificationSource.UserCorrection && t.CategoryId != categoryId)
            .CountAsync(ct);
        return new ApplyToAllOffer(matchType, label, count);
    }

    /// <summary>
    /// Makes the transaction's category a rule for its merchant (or the person, by account), then
    /// reclassifies every matching transaction. Corrections made one by one stay as they are.
    /// </summary>
    /// <returns>The rule and how many transactions changed category.</returns>
    public async Task<(MerchantRule Rule, int Changed)> ApplyToAllAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await FindAsync(transactionId, ct);
        if (transaction.CategoryId is not { } categoryId || Generalize(transaction) is not var (matchType, pattern, label))
        {
            throw new CorrectionException(NothingToApply, "The transaction has no category or nothing to generalize by.");
        }

        return await UpsertRuleAsync(matchType, pattern, label, categoryId, ct);
    }

    /// <summary>Labels every transaction from a merchant at once (first-import onboarding).</summary>
    public async Task<(MerchantRule Rule, int Changed)> LabelMerchantAsync(string merchantKey, Guid categoryId, CancellationToken ct)
    {
        await RequireCategoryAsync(categoryId, ct);
        if (!await db.Transactions.AnyAsync(t => t.MerchantKey == merchantKey, ct))
        {
            throw new CorrectionException(TransactionNotFound, "No transactions from that merchant.");
        }

        return await UpsertRuleAsync(RuleMatchType.Exact, merchantKey, merchantKey, categoryId, ct);
    }

    /// <summary>Deletes a user rule and reclassifies what it had classified.</summary>
    public async Task<int> DeleteRuleAsync(Guid ruleId, CancellationToken ct)
    {
        var rule = await db.MerchantRules.SingleOrDefaultAsync(r => r.Id == ruleId, ct)
            ?? throw new CorrectionException(RuleNotFound, "No such rule.");
        var affected = await db.Transactions.Where(t => t.ClassificationRuleId == ruleId).Select(t => t.Id).ToListAsync(ct);
        db.MerchantRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return await classifier.ClassifyAsync(affected, ct);
    }

    /// <summary>
    /// Marks incoming money as paying back spending in a category (or clears it with <c>null</c>):
    /// it then offsets that category instead of counting as income.
    /// </summary>
    public async Task<Transaction> SetOffsetsAsync(Guid transactionId, Guid? categoryId, CancellationToken ct)
    {
        var transaction = await FindAsync(transactionId, ct);
        if (transaction.Amount <= 0)
        {
            throw new CorrectionException(NotIncoming, "Only incoming money can pay back spending.");
        }

        if (categoryId is { } id)
        {
            var category = await RequireCategoryAsync(id, ct);
            if (category.Kind != CategoryKind.Spending)
            {
                throw new CorrectionException(NotSpending, "Only a spending category can be paid back.");
            }
        }

        transaction.OffsetsCategoryId = categoryId;
        transaction.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return transaction;
    }

    /// <summary>
    /// People are matched by account number (stable; the name is only a fallback when there is
    /// none), everything else by merchant key. Card terminals and invoices change accounts.
    /// </summary>
    internal static (RuleMatchType MatchType, string Pattern, string Label)? Generalize(Transaction transaction)
    {
        var byPerson = transaction.Kind is TransactionKind.BlikP2P or TransactionKind.PhoneTransfer
            || transaction.ClassificationReason is Reasons.AccountHolderIncoming or Reasons.AccountHolderOutgoing
            || transaction.Kind == TransactionKind.Transfer && transaction.ClassificationReason == Reasons.UserAccount;
        if (byPerson && AccountNumber.Hash(transaction.CounterpartyAccount) is { } hash)
        {
            return (RuleMatchType.CounterpartyAccount, hash, transaction.CounterpartyName);
        }

        return transaction.MerchantKey.Length > 0 && transaction.Kind != TransactionKind.OwnTransfer
            ? (RuleMatchType.Exact, transaction.MerchantKey, transaction.MerchantKey)
            : null;
    }

    private IQueryable<Transaction> Matching(RuleMatchType matchType, string pattern)
    {
        if (matchType == RuleMatchType.Exact)
        {
            return db.Transactions.Where(t => t.MerchantKey == pattern);
        }

        // Account hashes aren't stored; the candidates are few (one person's transfers).
        var ids = db.Transactions.AsNoTracking()
            .Where(t => t.CounterpartyAccount != null)
            .Select(t => new { t.Id, t.CounterpartyAccount })
            .AsEnumerable()
            .Where(t => AccountNumber.Hash(t.CounterpartyAccount) == pattern)
            .Select(t => t.Id)
            .ToList();
        return db.Transactions.Where(t => ids.Contains(t.Id));
    }

    private async Task<(MerchantRule Rule, int Changed)> UpsertRuleAsync(
        RuleMatchType matchType, string pattern, string label, Guid categoryId, CancellationToken ct)
    {
        var rule = await db.MerchantRules.SingleOrDefaultAsync(r => r.MatchType == matchType && r.Pattern == pattern, ct);
        if (rule is null)
        {
            rule = new MerchantRule
            {
                Id = Guid.CreateVersion7(),
                MatchType = matchType,
                Pattern = pattern,
                CategoryId = categoryId,
                Label = label.Length <= 200 ? label : label[..200],
                CreatedAt = clock.GetUtcNow(),
            };
            db.MerchantRules.Add(rule);
        }
        else
        {
            rule.CategoryId = categoryId;
        }

        await db.SaveChangesAsync(ct);
        var ids = await Matching(matchType, pattern).Select(t => t.Id).ToListAsync(ct);
        return (rule, await classifier.ClassifyAsync(ids, ct));
    }

    private async Task<Transaction> FindAsync(Guid id, CancellationToken ct) =>
        await db.Transactions.SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new CorrectionException(TransactionNotFound, "No such transaction.");

    /// <summary>A category the user can use: global or their own (the query filter hides others').</summary>
    private async Task<CategoryInfo> RequireCategoryAsync(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CorrectionException(CategoryNotFound, "No such category.");
        return new CategoryInfo(category.Id, category.Kind);
    }

    private sealed record CategoryInfo(Guid Id, CategoryKind Kind);
}
