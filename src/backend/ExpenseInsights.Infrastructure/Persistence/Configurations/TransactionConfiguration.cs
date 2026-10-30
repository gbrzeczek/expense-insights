using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseInsights.Infrastructure.Persistence.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.Property(t => t.ExternalId).HasMaxLength(64);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Amount).HasPrecision(14, 2);
        builder.Property(t => t.Currency).HasMaxLength(3);
        builder.Property(t => t.OriginalAmount).HasPrecision(14, 2);
        builder.Property(t => t.OriginalCurrency).HasMaxLength(3);
        builder.Property(t => t.BalanceAfter).HasPrecision(14, 2);
        builder.Property(t => t.MerchantKey).HasMaxLength(200);
        builder.Property(t => t.ClassificationSource).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.ClassificationReason).HasMaxLength(32);
        builder.Property(t => t.ClassificationReasonValue).HasMaxLength(200);

        // Postgres' row version: the background classifier never overwrites a correction made meanwhile.
        // Npgsql maps a uint row version to the system column xmin.
        builder.Property<uint>("Version").IsRowVersion();

        // The original CSV row, as JSONB (hard rule 5).
        builder.ComplexProperty(t => t.RawRow, raw => raw.ToJson());

        builder.HasIndex(t => new { t.AccountId, t.ExternalId }).IsUnique().HasFilter("\"ExternalId\" IS NOT NULL");
        builder.HasIndex(t => new { t.AccountId, t.Status, t.TransactionDate });
        builder.HasIndex(t => new { t.UserId, t.TransactionDate });
        builder.HasIndex(t => new { t.UserId, t.MerchantKey });
        builder.HasIndex(t => new { t.UserId, t.CategoryId });

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(t => t.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Transaction>().WithMany().HasForeignKey(t => t.OwnTransferPairId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Category>().WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(t => t.OffsetsCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MerchantRule>().WithMany().HasForeignKey(t => t.ClassificationRuleId).OnDelete(DeleteBehavior.SetNull);
    }
}
