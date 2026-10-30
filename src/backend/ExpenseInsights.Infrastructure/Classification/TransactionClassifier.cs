using ExpenseInsights.Classification;
using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Classification;

/// <summary>
/// Runs the classification pipeline over the current user's transactions: sets the merchant key and
/// the category from the rules, and records every change in <see cref="ClassificationHistory"/>.
/// A <see cref="ClassificationSource.UserCorrection"/> is never overwritten.
/// </summary>
public sealed class TransactionClassifier(AppDbContext db, TimeProvider clock)
{
    private const int BatchSize = 500;

    /// <summary>Classifies the given transactions, or all of the user's when <paramref name="transactionIds"/> is <c>null</c>.</summary>
    /// <returns>How many transactions changed category.</returns>
    public async Task<int> ClassifyAsync(IReadOnlyCollection<Guid>? transactionIds, CancellationToken ct)
    {
        var context = await LoadContextAsync(ct);
        var query = db.Transactions.AsQueryable();
        if (transactionIds is not null)
        {
            query = query.Where(t => transactionIds.Contains(t.Id));
        }

        var ids = await query.OrderBy(t => t.Id).Select(t => t.Id).ToListAsync(ct);
        var changed = 0;
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            changed += await ClassifyChunkAsync(chunk, context, ct);
        }

        return changed;
    }

    private async Task<int> ClassifyChunkAsync(Guid[] ids, Context context, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var batch = await db.Transactions.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
            var now = clock.GetUtcNow();
            var changed = batch.Count(t => Apply(t, context, now));
            try
            {
                await db.SaveChangesAsync(ct);
                return changed;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                // The user corrected one of them meanwhile: reload, which skips the correction.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>The rule matches for one transaction, best first: the classification and the suggestions.</summary>
    public async Task<IReadOnlyList<RuleMatch>> MatchesAsync(Transaction transaction, CancellationToken ct)
    {
        var context = await LoadContextAsync(ct);
        return RuleEngine.Evaluate(Input(transaction), context.Rules, GlobalRules.Default, context.Holder);
    }

    public static RuleInput Input(Transaction t) =>
        new(t.Kind, t.Amount, MerchantNormalizer.Normalize(t.CounterpartyName, t.MerchantHint), t.TitleRaw, AccountNumber.Hash(t.CounterpartyAccount));

    /// <summary>Records a category change in the history.</summary>
    internal void Record(Transaction transaction, Guid? previous, DateTimeOffset now) =>
        db.ClassificationHistory.Add(new ClassificationHistory
        {
            Id = Guid.CreateVersion7(),
            TransactionId = transaction.Id,
            PreviousCategoryId = previous,
            CategoryId = transaction.CategoryId,
            Source = transaction.ClassificationSource,
            Confidence = transaction.ClassificationConfidence,
            RuleId = transaction.ClassificationRuleId,
            ChangedAt = now,
        });

    private bool Apply(Transaction transaction, Context context, DateTimeOffset now)
    {
        var input = Input(transaction);
        transaction.MerchantKey = input.MerchantKey;
        if (transaction.ClassificationSource == ClassificationSource.UserCorrection)
        {
            transaction.ClassifiedAt ??= now;
            return false;
        }

        var matches = RuleEngine.Evaluate(input, context.Rules, GlobalRules.Default, context.Holder);
        var best = matches.Count > 0 ? matches[0] : null;
        var previous = transaction.CategoryId;
        var previousSource = transaction.ClassificationSource;

        transaction.CategoryId = best?.CategoryId;
        transaction.ClassificationSource = best?.Source ?? ClassificationSource.Unclassified;
        transaction.ClassificationConfidence = best?.Confidence ?? 0;
        transaction.ClassificationRuleId = best?.RuleId;
        transaction.ClassificationReason = best?.Reason;
        transaction.ClassificationReasonValue = best?.ReasonValue;
        transaction.ClassifiedAt = now;

        if (previous == transaction.CategoryId && previousSource == transaction.ClassificationSource)
        {
            return false;
        }

        Record(transaction, previous, now);
        return previous != transaction.CategoryId;
    }

    private async Task<Context> LoadContextAsync(CancellationToken ct)
    {
        var rules = await db.MerchantRules.AsNoTracking().ToListAsync(ct);
        var userId = db.CurrentUserId;
        var name = await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).SingleOrDefaultAsync(ct);
        return new Context(rules, RuleEngine.NameWords(name));
    }

    private sealed record Context(IReadOnlyCollection<MerchantRule> Rules, IReadOnlyCollection<string> Holder);
}
