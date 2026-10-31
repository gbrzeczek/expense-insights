using ExpenseInsights.Classification;
using ExpenseInsights.Classification.Tiers;
using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Classification;

/// <summary>
/// Runs the classification pipeline over the current user's transactions: sets the merchant key and
/// the category from the rules, then from the tiers after them (plan 07) for what the rules leave
/// open, and records every change in <see cref="ClassificationHistory"/>. A
/// <see cref="ClassificationSource.UserCorrection"/> is never overwritten.
/// </summary>
/// <param name="db">The current user's data.</param>
/// <param name="clock">For timestamps.</param>
/// <param name="tiers">The tiers after the rules; <see cref="ClassificationTiers.RulesAndMemory"/> if not given.</param>
public sealed class TransactionClassifier(AppDbContext db, TimeProvider clock, ClassificationTiers? tiers = null)
{
    private readonly ClassificationTiers _tiers = tiers ?? ClassificationTiers.RulesAndMemory;

    private const int BatchSize = 500;

    /// <summary>Classifies the given transactions, or all of the user's when <paramref name="transactionIds"/> is <c>null</c>.</summary>
    /// <returns>How many transactions changed category.</returns>
    public async Task<int> ClassifyAsync(IReadOnlyCollection<Guid>? transactionIds, CancellationToken ct)
    {
        var context = await LoadContextAsync(withLabels: true, ct);
        var query = db.Transactions.AsQueryable();
        if (transactionIds is not null)
        {
            query = query.Where(t => transactionIds.Contains(t.Id));
        }

        var ids = await query.OrderBy(t => t.Id).Select(t => t.Id).ToListAsync(ct);
        var pipeline = Pipeline(context);
        var changed = 0;
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            changed += await ClassifyChunkAsync(chunk, context, pipeline, ct);
        }

        return changed;
    }

    private async Task<int> ClassifyChunkAsync(Guid[] ids, Context context, TierPipeline pipeline, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var batch = await db.Transactions.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
            var open = batch.Where(t => t.ClassificationSource != ClassificationSource.UserCorrection).ToList();
            var inputs = open.Select(Input).ToList();
            var matches = inputs.Select(i => RuleEngine.Evaluate(i, context.Rules, GlobalRules.Default, context.Holder)).ToList();
            var results = await pipeline.ClassifyAsync(inputs, matches, ct);

            var now = clock.GetUtcNow();
            foreach (var corrected in batch.Except(open))
            {
                corrected.MerchantKey = Input(corrected).MerchantKey;
                corrected.ClassifiedAt ??= now;
            }

            var changed = open.Select((t, i) => Apply(t, inputs[i].MerchantKey, results[i], now)).Count(c => c);
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
        var context = await LoadContextAsync(withLabels: false, ct);
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

    /// <summary>Sets the pipeline's result on a transaction that isn't a user correction; whether its category changed.</summary>
    private bool Apply(Transaction transaction, string merchantKey, RuleMatch? best, DateTimeOffset now)
    {
        transaction.MerchantKey = merchantKey;
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

    /// <summary>The tiers for one run: A over the user's labels, C with the model and the stored neighbours.</summary>
    private TierPipeline Pipeline(Context context)
    {
        var memory = _tiers.Memory && context.Labelled.Count > 0 ? new MemoryClassifier(context.Labelled) : null;
        var embedding = _tiers is { Embedder: { } embedder, Anchors: { } anchors }
            ? new EmbeddingClassifier(embedder, anchors, context.Labelled.Count > 0 ? new StoredNeighbours(db, embedder, context.Labelled, clock) : null)
            : null;
        return new TierPipeline(memory, embedding);
    }

    private async Task<Context> LoadContextAsync(bool withLabels, CancellationToken ct)
    {
        var rules = await db.MerchantRules.AsNoTracking().ToListAsync(ct);
        var userId = db.CurrentUserId;
        var name = await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).SingleOrDefaultAsync(ct);

        // What the user confirmed: their corrections and what their own rules classified.
        var labelled = !withLabels ? [] : await db.Transactions.AsNoTracking()
            .Where(t => t.CategoryId != null && t.MerchantKey != ""
                && (t.ClassificationSource == ClassificationSource.UserCorrection || t.ClassificationSource == ClassificationSource.UserRule))
            .Select(t => new LabelledExample(t.MerchantKey, t.Amount, t.Kind, t.CategoryId!.Value))
            .ToListAsync(ct);
        return new Context(rules, RuleEngine.NameWords(name), labelled);
    }

    private sealed record Context(IReadOnlyCollection<MerchantRule> Rules, IReadOnlyCollection<string> Holder, IReadOnlyList<LabelledExample> Labelled);
}
