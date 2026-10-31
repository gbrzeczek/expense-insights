using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Core.Classification;

/// <summary>
/// The embedding of one of the user's labelled merchant keys (tier C, plan 07): the neighbours an
/// unknown merchant is compared with. One per merchant key and model; computed when the merchant
/// is first labelled, recomputed when the model changes. The vector itself (unit length,
/// <c>pgvector</c> <c>vector(384)</c>) is a shadow property of the infrastructure.
/// </summary>
public sealed class MerchantEmbedding : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>From the merchant normalizer, as on <see cref="Transactions.Transaction.MerchantKey"/>.</summary>
    public required string MerchantKey { get; set; }

    /// <summary>Which model made it (repository@revision): embeddings of different models don't compare.</summary>
    public required string Model { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
