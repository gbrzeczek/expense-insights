using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Users;
using ExpenseInsights.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpenseInsights.Infrastructure.Tests.Users;

public sealed class UserSignInTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task InvitedEmail_CreatesAUser_AndAcceptsTheInvitation()
    {
        var email = NewEmail();
        var invitationId = await Postgres.InviteAsync(email, CancellationToken);

        var user = await SignInAsync(Google(email.ToUpperInvariant(), name: " Anna Nowak "));

        Assert.NotNull(user);
        Assert.Equal(UserRole.User, user.Role);
        Assert.Equal(email, user.Email);
        Assert.Equal("Anna Nowak", user.DisplayName);
        await using var db = CreateDbContext();
        var invitation = await db.Invitations.SingleAsync(i => i.Id == invitationId, CancellationToken);
        Assert.Equal(user.Id, invitation.UserId);
        Assert.NotNull(invitation.AcceptedAt);
    }

    [Fact]
    public async Task KnownLogin_SignsInTheSameUser()
    {
        var email = NewEmail();
        await Postgres.InviteAsync(email, CancellationToken);
        var identity = Google(email);

        var first = await SignInAsync(identity);
        // The Google account's email may change later; the subject is what identifies it.
        var second = await SignInAsync(identity with { Email = "changed@example.test" });

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal("changed@example.test", second.Email);
    }

    [Fact]
    public async Task UninvitedEmail_IsDenied_AndCreatesNothing()
    {
        await using var before = CreateDbContext();
        var users = await before.Users.CountAsync(CancellationToken);

        var user = await SignInAsync(Google(NewEmail()));

        Assert.Null(user);
        await using var after = CreateDbContext();
        Assert.Equal(users, await after.Users.CountAsync(CancellationToken));
    }

    [Fact]
    public async Task UnverifiedEmail_IsDenied()
    {
        var email = NewEmail();
        await Postgres.InviteAsync(email, CancellationToken);

        Assert.Null(await SignInAsync(Google(email) with { EmailVerified = false }));
        Assert.Null(await SignInAsync(Google(email) with { Email = null }));
    }

    [Fact]
    public async Task AcceptedInvitation_CantBeUsedByAnotherGoogleAccount()
    {
        var email = NewEmail();
        await Postgres.InviteAsync(email, CancellationToken);
        await SignInAsync(Google(email));

        Assert.Null(await SignInAsync(Google(email)));
    }

    [Fact]
    public async Task OwnerEmail_LinksToTheSeededOwner()
    {
        await using (var db = CreateDbContext())
        {
            await OwnerInvitation.EnsureAsync(db, "Owner@Example.test", TimeProvider.System, CancellationToken);
            // Idempotent: startup runs it every time.
            await OwnerInvitation.EnsureAsync(db, "owner@example.test", TimeProvider.System, CancellationToken);
        }

        var owner = await SignInAsync(Google("owner@example.test"));

        Assert.Equal(User.OwnerId, owner!.Id);
        Assert.Equal(UserRole.Owner, owner.Role);
        await using var check = CreateDbContext();
        Assert.Equal(1, await check.Invitations.CountAsync(i => i.Email == "owner@example.test", CancellationToken));
        Assert.Equal(User.OwnerId, (await check.ExternalLogins.SingleAsync(l => l.Provider == ExternalLogin.Google && l.UserId == User.OwnerId, CancellationToken)).UserId);

        // After the owner signed in, a changed configuration doesn't take their account away.
        await using (var db = CreateDbContext())
        {
            await OwnerInvitation.EnsureAsync(db, "someone-else@example.test", TimeProvider.System, CancellationToken);
        }

        await using var final = CreateDbContext();
        Assert.Equal("owner@example.test", (await final.Invitations.SingleAsync(i => i.UserId == User.OwnerId, CancellationToken)).Email);
    }

    private async Task<User?> SignInAsync(ExternalIdentity identity)
    {
        await using var db = CreateDbContext(Guid.Empty);
        return await new UserSignIn(db, TimeProvider.System, NullLogger<UserSignIn>.Instance).SignInAsync(identity, CancellationToken);
    }

    private static ExternalIdentity Google(string email, string? name = null) =>
        new(ExternalLogin.Google, Guid.NewGuid().ToString("N"), email, EmailVerified: true, name);

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";
}
