using ExpenseInsights.Core.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseInsights.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.DisplayName).HasMaxLength(200);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);

        builder.HasData(new User
        {
            Id = User.OwnerId,
            Role = UserRole.Owner,
            CreatedAt = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero),
        });
    }
}
