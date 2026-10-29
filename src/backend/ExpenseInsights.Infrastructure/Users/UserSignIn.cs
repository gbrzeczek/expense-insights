using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseInsights.Infrastructure.Users;

/// <summary>What an identity provider says about the person signing in.</summary>
/// <param name="Provider">For example <see cref="ExternalLogin.Google"/>.</param>
/// <param name="Subject">The provider's stable account id.</param>
/// <param name="Email">The account's email, if the provider shared it.</param>
/// <param name="EmailVerified">Only verified emails are matched against invitations.</param>
/// <param name="DisplayName">The account's name, for display.</param>
public sealed record ExternalIdentity(string Provider, string Subject, string? Email, bool EmailVerified, string? DisplayName);

/// <summary>
/// Maps an external sign-in to a user. A known login signs in its user; otherwise only an invited,
/// verified email can create an account (or link to the user its invitation points at, which is how
/// the seeded owner gets their Google login).
/// </summary>
public sealed partial class UserSignIn(AppDbContext db, TimeProvider clock, ILogger<UserSignIn> logger)
{
    /// <returns>The signed-in user, or <c>null</c> when the person has no access.</returns>
    public async Task<User?> SignInAsync(ExternalIdentity identity, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var login = await db.ExternalLogins
            .SingleOrDefaultAsync(l => l.Provider == identity.Provider && l.Subject == identity.Subject, ct);
        if (login is not null)
        {
            var known = await db.Users.SingleAsync(u => u.Id == login.UserId, ct);
            login.LastSignInAt = now;
            UpdateProfile(known, identity);
            await db.SaveChangesAsync(ct);
            return known;
        }

        if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
        {
            LogDenied(logger, identity.Provider, "no verified email");
            return null;
        }

        var email = Invitation.NormalizeEmail(identity.Email);
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Email == email, ct);
        if (invitation is null)
        {
            LogDenied(logger, identity.Provider, "not invited");
            return null;
        }

        User user;
        if (invitation.UserId is { } userId)
        {
            user = await db.Users.SingleAsync(u => u.Id == userId, ct);
            if (await db.ExternalLogins.AnyAsync(l => l.UserId == userId && l.Provider == identity.Provider, ct))
            {
                // The invitation was accepted with another account of the same provider.
                LogDenied(logger, identity.Provider, "invitation already used");
                return null;
            }
        }
        else
        {
            user = new User { Id = Guid.CreateVersion7(), Role = UserRole.User, CreatedAt = now };
            db.Users.Add(user);
            invitation.UserId = user.Id;
        }

        invitation.AcceptedAt ??= now;
        UpdateProfile(user, identity);
        db.ExternalLogins.Add(new ExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Provider = identity.Provider,
            Subject = identity.Subject,
            CreatedAt = now,
            LastSignInAt = now,
        });
        await db.SaveChangesAsync(ct);
        LogLinked(logger, identity.Provider, user.Id);
        return user;
    }

    private static void UpdateProfile(User user, ExternalIdentity identity)
    {
        if (identity.EmailVerified && !string.IsNullOrWhiteSpace(identity.Email))
        {
            user.Email = Invitation.NormalizeEmail(identity.Email);
        }

        if (!string.IsNullOrWhiteSpace(identity.DisplayName))
        {
            user.DisplayName = identity.DisplayName.Trim();
        }
    }

    // No emails or subjects in logs: they identify people.
    [LoggerMessage(Level = LogLevel.Information, Message = "Sign-in with {Provider} denied: {Reason}")]
    private static partial void LogDenied(ILogger logger, string provider, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Linked a {Provider} login to user {UserId}")]
    private static partial void LogLinked(ILogger logger, string provider, Guid userId);
}
