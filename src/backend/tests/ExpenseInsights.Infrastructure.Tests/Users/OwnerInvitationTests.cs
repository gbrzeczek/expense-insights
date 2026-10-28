using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Users;
using ExpenseInsights.Testing;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Tests.Users;

public sealed class OwnerInvitationTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task ChangedOwnerEmail_ReplacesTheInvitation_UntilTheOwnerSignsIn()
    {
        await EnsureAsync("first@example.test");
        await EnsureAsync("second@example.test");

        await using var db = CreateDbContext();
        var invitation = await db.Invitations.SingleAsync(i => i.UserId == User.OwnerId, CancellationToken);
        Assert.Equal("second@example.test", invitation.Email);
        Assert.False(await db.Invitations.AnyAsync(i => i.Email == "first@example.test", CancellationToken));
    }

    [Fact]
    public async Task OwnerEmail_InvitedAsARegularUser_BecomesTheOwnersInvitation()
    {
        await Postgres.InviteAsync("invited-first@example.test", CancellationToken);

        await EnsureAsync("invited-first@example.test");

        await using var db = CreateDbContext();
        var invitation = await db.Invitations.SingleAsync(i => i.Email == "invited-first@example.test", CancellationToken);
        Assert.Equal(User.OwnerId, invitation.UserId);
    }

    private async Task EnsureAsync(string email)
    {
        await using var db = CreateDbContext();
        await OwnerInvitation.EnsureAsync(db, email, TimeProvider.System, CancellationToken);
    }
}
