using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseInsights.Infrastructure.Persistence.Configurations;

internal sealed class ClassificationHistoryConfiguration : IEntityTypeConfiguration<ClassificationHistory>
{
    public void Configure(EntityTypeBuilder<ClassificationHistory> builder)
    {
        builder.ToTable("ClassificationHistory");
        builder.Property(h => h.Source).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(h => new { h.TransactionId, h.ChangedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Transaction>().WithMany().HasForeignKey(h => h.TransactionId).OnDelete(DeleteBehavior.Cascade);
        // An audit trail: rules can be deleted, the history keeps the id.
    }
}
