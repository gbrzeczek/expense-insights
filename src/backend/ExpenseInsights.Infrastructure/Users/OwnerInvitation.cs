using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Users;

/// <summary>Seeds the allow-list with the owner's email from configuration (<c>Auth:OwnerEmail</c>).</summary>
public static class OwnerInvitation
{
    /// <summary>
    /// Makes sure an invitation for <paramref name="ownerEmail"/> points at the seeded owner. Until the
    /// owner signs in, a changed address replaces the old one; afterwards the owner keeps their login.
    /// </summary>
    public static async Task EnsureAsync(AppDbContext db, string ownerEmail, TimeProvider clock, CancellationToken ct)
    {
        var email = Invitation.NormalizeEmail(ownerEmail);
        var owners = await db.Invitations.SingleOrDefaultAsync(i => i.UserId == User.OwnerId, ct);
        if (owners is not null && (owners.Email == email || owners.AcceptedAt is not null))
        {
            return;
        }

        var sameEmail = await db.Invitations.SingleOrDefaultAsync(i => i.Email == email, ct);
        if (sameEmail?.UserId is not null)
        {
            throw new InvalidOperationException("The owner's email is already used by another user.");
        }

        if (owners is not null)
        {
            db.Invitations.Remove(owners);
        }

        if (sameEmail is not null)
        {
            // Invited as a regular user before; this becomes the owner's invitation.
            sameEmail.UserId = User.OwnerId;
        }
        else
        {
            db.Invitations.Add(new Invitation
            {
                Id = Guid.CreateVersion7(),
                Email = email,
                UserId = User.OwnerId,
                CreatedAt = clock.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
