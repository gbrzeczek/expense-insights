using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseInsights.Infrastructure.Persistence.Configurations;

internal sealed class MerchantRuleConfiguration : IEntityTypeConfiguration<MerchantRule>
{
    public void Configure(EntityTypeBuilder<MerchantRule> builder)
    {
        builder.Property(r => r.MatchType).HasConversion<string>().HasMaxLength(24);
        builder.Property(r => r.Pattern).HasMaxLength(200);
        builder.Property(r => r.Label).HasMaxLength(200);

        builder.HasIndex(r => new { r.UserId, r.MatchType, r.Pattern }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>().WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
