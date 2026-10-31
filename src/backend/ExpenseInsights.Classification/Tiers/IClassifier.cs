using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Classification.Tiers;

/// <summary>A tier's answer for one transaction.</summary>
/// <param name="CategoryId">The category.</param>
/// <param name="Source"><see cref="ClassificationSource.Similarity"/> or <see cref="ClassificationSource.Embedding"/>.</param>
/// <param name="Confidence">Calibrated, 0–1: "0.9" means roughly 90% correct on the fake labels.</param>
/// <param name="Reason">Why, as an i18n code; see <see cref="Reasons"/>.</param>
/// <param name="ReasonValue">For display: the nearest labelled merchant or the matched category anchor.</param>
public sealed record TierResult(Guid CategoryId, ClassificationSource Source, double Confidence, string Reason, string? ReasonValue)
{
    public RuleMatch ToMatch() => new(CategoryId, Source, Confidence, Reason, ReasonValue, null);
}

/// <summary>
/// A classification tier after the rules (plan 07). Works on a batch, because the embedding tier is
/// only efficient that way; returns one result per input, <c>null</c> where it has no opinion.
/// </summary>
public interface IClassifier
{
    Task<IReadOnlyList<TierResult?>> ClassifyAsync(IReadOnlyList<RuleInput> batch, CancellationToken ct);
}

/// <summary>A transaction whose category the user confirmed: a correction or a rule they made.</summary>
/// <param name="MerchantKey">From <see cref="MerchantNormalizer"/>.</param>
/// <param name="Amount">Signed; examples only count for transactions in the same direction.</param>
/// <param name="Kind">A tie-breaker: a card payment to "P.H.U. Anna Nowak" isn't a transfer to the person Anna Nowak.</param>
/// <param name="CategoryId">The confirmed category.</param>
public sealed record LabelledExample(string MerchantKey, decimal Amount, TransactionKind Kind, Guid CategoryId)
{
    /// <summary>Paying a merchant (card, BLIK online) as opposed to a transfer to an account.</summary>
    public static bool IsPayment(TransactionKind kind) => kind is TransactionKind.Card or TransactionKind.BlikOnline;
}
