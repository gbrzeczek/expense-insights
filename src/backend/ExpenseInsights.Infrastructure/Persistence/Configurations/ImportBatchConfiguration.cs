using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseInsights.Infrastructure.Persistence.Configurations;

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.Property(b => b.BankId).HasMaxLength(32);
        builder.Property(b => b.FileName).HasMaxLength(255);
        builder.Property(b => b.FileHash).HasMaxLength(64);
        builder.Property(b => b.DocumentId).HasMaxLength(64);
        builder.Property(b => b.TotalsCheck).HasConversion<string>().HasMaxLength(16);
        builder.Property(b => b.BalanceCheck).HasConversion<string>().HasMaxLength(16);

        builder.ComplexCollection(b => b.Warnings, issues => issues.ToJson());
        builder.ComplexCollection(b => b.Errors, issues => issues.ToJson());

        builder.HasIndex(b => new { b.UserId, b.FileHash }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
