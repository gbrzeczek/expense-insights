using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseInsights.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.Property(a => a.BankId).HasMaxLength(32);
        builder.Property(a => a.Name).HasMaxLength(200);
        builder.Property(a => a.NumberMasked).HasMaxLength(32);
        builder.Property(a => a.NumberHash).HasMaxLength(64);
        builder.Property(a => a.Currency).HasMaxLength(3);

        builder.HasIndex(a => new { a.UserId, a.BankId, a.NumberHash }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
