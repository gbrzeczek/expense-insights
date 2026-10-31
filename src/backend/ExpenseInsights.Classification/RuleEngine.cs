using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Classification;

/// <summary>What the rules look at. Bank-agnostic: it comes from the normalized transaction.</summary>
/// <param name="Kind">The transaction kind; <see cref="TransactionKind.OwnTransfer"/> means the counterparty is one of the user's accounts in the export.</param>
/// <param name="Amount">Signed: expenses negative.</param>
/// <param name="MerchantKey">From <see cref="MerchantNormalizer"/>.</param>
/// <param name="Title">The transaction title as in the file.</param>
/// <param name="CounterpartyAccountHash">The counterparty account's hash (<c>AccountNumber.Hash</c>), if it has one.</param>
public sealed record RuleInput(TransactionKind Kind, decimal Amount, string MerchantKey, string Title, string? CounterpartyAccountHash);

/// <summary>One way the transaction could be classified, best first in <see cref="RuleEngine.Evaluate"/>.</summary>
/// <param name="CategoryId">The category.</param>
/// <param name="Source">A user or a global rule.</param>
/// <param name="Confidence">0–1; see <see cref="Core.Classification.Confidence.ReviewBelow"/>.</param>
/// <param name="Reason">Why, as an i18n code (<c>merchantExact</c>); see <see cref="Reasons"/>.</param>
/// <param name="ReasonValue">The matched pattern or keyword, for display. Never an account number.</param>
/// <param name="RuleId">The user rule, if one matched.</param>
public sealed record RuleMatch(Guid CategoryId, ClassificationSource Source, double Confidence, string Reason, string? ReasonValue, Guid? RuleId);

public static class Reasons
{
    public const string OwnAccount = "ownAccount";
    public const string Refund = "refund";
    public const string UserAccount = "userAccount";
    public const string MerchantExact = "merchantExact";
    public const string MerchantContains = "merchantContains";
    public const string MerchantSimilar = "merchantSimilar";
    public const string TitleKeyword = "titleKeyword";
    public const string AccountHolderIncoming = "accountHolderIncoming";
    public const string AccountHolderOutgoing = "accountHolderOutgoing";
    public const string PersonOutgoing = "personOutgoing";
    public const string PersonIncoming = "personIncoming";

    /// <summary>Tiers A and C: like one of the user's labelled merchants (the value).</summary>
    public const string SimilarTo = "similarTo";

    /// <summary>Tier C: close to a category's anchor (the value: the example merchant or description).</summary>
    public const string CategoryAnchor = "categoryAnchor";
}

/// <summary>
/// The rule tiers of the classification pipeline, in order: the user's own accounts and refunds,
/// the user's rules, the account holder's own name, transfers to and from people, then the global
/// merchant and title rules. Deterministic and pure: no I/O.
/// </summary>
public static class RuleEngine
{
    /// <summary>Similarity needed for a merchant to count as matching a pattern.</summary>
    public const double SimilarityThreshold = 0.55;

    /// <summary>All matches, best first, one per category. The first one is the classification.</summary>
    /// <param name="input">The transaction.</param>
    /// <param name="userRules">The user's rules.</param>
    /// <param name="globalRules">The global rules; <see cref="GlobalRules.Default"/> in the app.</param>
    /// <param name="accountHolder">The user's name split into folded words, to spot their own name as the counterparty.</param>
    public static IReadOnlyList<RuleMatch> Evaluate(
        RuleInput input,
        IReadOnlyCollection<MerchantRule> userRules,
        GlobalRules globalRules,
        IReadOnlyCollection<string>? accountHolder = null)
    {
        var matches = new List<RuleMatch>();
        var title = MerchantNormalizer.Fold(input.Title);
        var keyTrigrams = TrigramSimilarity.Trigrams(input.MerchantKey);

        // 1. Certain from the export itself.
        if (input.Kind == TransactionKind.OwnTransfer)
        {
            matches.Add(System(CategoryCatalog.OwnTransfer, 1.0, Reasons.OwnAccount));
        }

        if (input.Amount > 0 && input.Kind is TransactionKind.Card or TransactionKind.BlikOnline)
        {
            matches.Add(System(CategoryCatalog.Refunds, 0.9, Reasons.Refund));
        }

        // 2. The user's rules: higher priority first, then the closer match.
        matches.AddRange(userRules
            .Select(rule => (Rule: rule, Match: MatchUserRule(rule, input, title, keyTrigrams)))
            .Where(m => m.Match is not null)
            .OrderByDescending(m => m.Rule.Priority)
            .ThenByDescending(m => m.Match!.Confidence)
            .ThenByDescending(m => m.Rule.Pattern.Length)
            .Select(m => m.Match!));

        // 3. The account holder's own name with an account that isn't in the export: a business or
        // another bank's account. Incoming is income (e.g. from the user's business), outgoing most
        // likely their own other account. Both are only a guess: they go to review.
        if (input.Kind != TransactionKind.OwnTransfer && IsAccountHolder(input.MerchantKey, accountHolder))
        {
            matches.Add(input.Amount > 0
                ? System(CategoryCatalog.OtherIncome, 0.5, Reasons.AccountHolderIncoming)
                : System(CategoryCatalog.OwnTransfer, 0.5, Reasons.AccountHolderOutgoing));
        }

        // 4. Transfers to and from people (decision D11).
        if (input.Kind is TransactionKind.BlikP2P or TransactionKind.PhoneTransfer)
        {
            matches.Add(input.Amount < 0
                ? System(CategoryCatalog.TransfersToPeople, 0.9, Reasons.PersonOutgoing)
                : System(CategoryCatalog.TransfersFromPeople, 0.9, Reasons.PersonIncoming));
        }

        // 5. Global rules. Money in from a merchant is a refund rather than spending.
        var global = MatchGlobal(input, title, keyTrigrams, globalRules);
        matches.AddRange(global.Select(m =>
            input.Amount > 0 && IsSpending(m.CategoryId) ? m with { CategoryId = CategoryCatalog.Refunds.Id, Confidence = Math.Min(m.Confidence, 0.75), Reason = Reasons.Refund } : m));

        return [.. matches.DistinctBy(m => m.CategoryId)];
    }

    /// <summary>Whether every word of the holder's name (at least two) is in the counterparty.</summary>
    public static bool IsAccountHolder(string merchantKey, IReadOnlyCollection<string>? accountHolder)
    {
        if (accountHolder is not { Count: >= 2 })
        {
            return false;
        }

        var words = merchantKey.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return accountHolder.All(w => words.Contains(w, StringComparer.Ordinal));
    }

    /// <summary>The words of a person's name as <see cref="IsAccountHolder"/> compares them.</summary>
    public static IReadOnlyCollection<string> NameWords(string? name) =>
        name is null
            ? []
            : MerchantNormalizer.Fold(name).Split([' ', '-', '.'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2).Distinct().ToList();

    private static RuleMatch? MatchUserRule(MerchantRule rule, RuleInput input, string title, HashSet<string> keyTrigrams)
    {
        double? confidence = rule.MatchType switch
        {
            RuleMatchType.CounterpartyAccount when input.CounterpartyAccountHash == rule.Pattern => 1.0,
            RuleMatchType.Exact when input.MerchantKey == rule.Pattern => 1.0,
            RuleMatchType.Contains when StartsWord(input.MerchantKey, rule.Pattern) => 0.95,
            RuleMatchType.TitleKeyword when StartsWord(title, rule.Pattern) => 0.9,
            RuleMatchType.Trigram when TrigramSimilarity.Similarity(keyTrigrams, TrigramSimilarity.Trigrams(rule.Pattern)) is var s && s >= SimilarityThreshold => s * 0.95,
            _ => null,
        };
        var reason = rule.MatchType switch
        {
            RuleMatchType.CounterpartyAccount => Reasons.UserAccount,
            RuleMatchType.Exact => Reasons.MerchantExact,
            RuleMatchType.Contains => Reasons.MerchantContains,
            RuleMatchType.TitleKeyword => Reasons.TitleKeyword,
            _ => Reasons.MerchantSimilar,
        };
        // The pattern of an account rule is a hash: show the rule's label instead.
        var value = rule.MatchType == RuleMatchType.CounterpartyAccount ? rule.Label : rule.Pattern;
        return confidence is { } c ? new RuleMatch(rule.CategoryId, ClassificationSource.UserRule, c, reason, value, rule.Id) : null;
    }

    private static List<RuleMatch> MatchGlobal(RuleInput input, string title, HashSet<string> keyTrigrams, GlobalRules globalRules)
    {
        var found = new List<(RuleMatch Match, int Length)>();
        foreach (var rule in globalRules.Rules)
        {
            if (!rule.Applies(input.Amount) && !(input.Amount > 0 && rule.MatchType != RuleMatchType.TitleKeyword && IsSpending(rule.CategoryId)))
            {
                continue;
            }

            var (confidence, reason) = rule.MatchType switch
            {
                RuleMatchType.Exact when input.MerchantKey == rule.Pattern => (0.95, Reasons.MerchantExact),
                RuleMatchType.Contains when input.MerchantKey == rule.Pattern => (0.95, Reasons.MerchantExact),
                RuleMatchType.Contains when StartsWord(input.MerchantKey, rule.Pattern) => (0.9, Reasons.MerchantContains),
                RuleMatchType.TitleKeyword when StartsWord(title, rule.Pattern) => (0.85, Reasons.TitleKeyword),
                _ => (0.0, ""),
            };
            if (confidence > 0)
            {
                found.Add((new RuleMatch(rule.CategoryId, ClassificationSource.GlobalRule, confidence, reason, rule.Pattern, null), rule.Pattern.Length));
            }
        }

        // Only when nothing matched outright: a misspelt or truncated merchant ("mediaexpet").
        if (found.Count == 0 && input.MerchantKey.Length >= 4)
        {
            foreach (var (rule, trigrams) in globalRules.Merchants)
            {
                if (rule.Pattern.Length < 4 || !(rule.Applies(input.Amount) || input.Amount > 0 && IsSpending(rule.CategoryId)))
                {
                    continue;
                }

                var similarity = TrigramSimilarity.Similarity(keyTrigrams, trigrams);
                if (similarity >= SimilarityThreshold)
                {
                    found.Add((new RuleMatch(rule.CategoryId, ClassificationSource.GlobalRule, similarity * 0.85, Reasons.MerchantSimilar, rule.Pattern, null), 0));
                }
            }
        }

        // The closest, then the most specific (longest) pattern: "uber eats" before "uber".
        return [.. found.OrderByDescending(f => f.Match.Confidence).ThenByDescending(f => f.Length).Select(f => f.Match)];
    }

    /// <summary>Whether <paramref name="pattern"/> occurs in <paramref name="text"/> at the start of a word.</summary>
    internal static bool StartsWord(string text, string pattern)
    {
        if (pattern.Length == 0)
        {
            return false;
        }

        for (var i = text.IndexOf(pattern, StringComparison.Ordinal); i >= 0; i = text.IndexOf(pattern, i + 1, StringComparison.Ordinal))
        {
            if (i == 0 || !char.IsLetterOrDigit(text[i - 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly Dictionary<Guid, CategoryKind> Kinds = CategoryCatalog.All.ToDictionary(c => c.Id, c => c.Kind);

    private static bool IsSpending(Guid categoryId) => Kinds.TryGetValue(categoryId, out var kind) && kind == CategoryKind.Spending;

    private static RuleMatch System(CatalogCategory category, double confidence, string reason) =>
        new(category.Id, ClassificationSource.GlobalRule, confidence, reason, null, null);
}
