using ExpenseInsights.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseInsights.Infrastructure.Persistence.Configurations;

internal sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.Property(l => l.Provider).HasMaxLength(32);
        builder.Property(l => l.Subject).HasMaxLength(255);

        builder.HasIndex(l => new { l.Provider, l.Subject }).IsUnique();
        // One login per provider and user.
        builder.HasIndex(l => new { l.UserId, l.Provider }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
