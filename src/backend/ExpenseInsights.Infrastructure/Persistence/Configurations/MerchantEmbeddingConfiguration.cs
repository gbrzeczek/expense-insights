using ExpenseInsights.Classification.Embeddings;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Classification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;

namespace ExpenseInsights.Infrastructure.Persistence.Configurations;

internal sealed class MerchantEmbeddingConfiguration : IEntityTypeConfiguration<MerchantEmbedding>
{
    public void Configure(EntityTypeBuilder<MerchantEmbedding> builder)
    {
        builder.Property(e => e.MerchantKey).HasMaxLength(200);
        builder.Property(e => e.Model).HasMaxLength(100);
        builder.Property<Vector>(StoredNeighbours.VectorProperty).HasColumnType($"vector({E5Model.Dimensions})").IsRequired();

        // No HNSW index: neighbours are searched within one user's few hundred labelled merchants,
        // where an exact scan is fast, and a filtered HNSW scan could miss them (plan 07).
        builder.HasIndex(e => new { e.UserId, e.MerchantKey, e.Model }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
