using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseInsights.Infrastructure.Persistence.Configurations;

internal sealed class CategorySettingConfiguration : IEntityTypeConfiguration<CategorySetting>
{
    public void Configure(EntityTypeBuilder<CategorySetting> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(60);

        builder.HasIndex(s => new { s.UserId, s.CategoryId }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>().WithMany().HasForeignKey(s => s.CategoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
