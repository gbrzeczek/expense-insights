using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseInsights.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    /// <summary>When the global taxonomy was seeded; fixed so the seed data never changes the model.</summary>
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(c => c.Key).HasMaxLength(40);
        builder.Property(c => c.Name).HasMaxLength(60);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(c => c.Key).IsUnique().HasFilter("\"Key\" IS NOT NULL");
        builder.HasIndex(c => new { c.UserId, c.ParentId });
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasData(CategoryCatalog.All.Select(c => new Category
        {
            Id = c.Id,
            Key = c.Key,
            Kind = c.Kind,
            SortOrder = c.SortOrder,
            CreatedAt = SeededAt,
        }));
    }
}
