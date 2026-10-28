namespace ExpenseInsights.Core.Users;

/// <summary>
/// The allow-list: only invited email addresses can create an account. Managed by the owner, so
/// not filtered by user.
/// </summary>
public sealed class Invitation
{
    public Guid Id { get; set; }

    /// <summary>Normalized with <see cref="NormalizeEmail"/>; unique.</summary>
    public required string Email { get; set; }

    /// <summary>
    /// The user this invitation signs in as: set on acceptance, or up front for the owner's
    /// invitation (the seeded owner).
    /// </summary>
    public Guid? UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
