namespace ExpenseInsights.Core.Users;

/// <summary>An app user, signed in through an <see cref="ExternalLogin"/> (Google).</summary>
public sealed class User
{
    /// <summary>
    /// The owner, seeded by the initial migration. The owner's invitation points at this user, so
    /// the owner's first sign-in links to it instead of creating a second user.
    /// </summary>
    public static readonly Guid OwnerId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; }

    /// <summary>From the identity provider; <c>null</c> for the owner until the first sign-in.</summary>
    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public DateTimeOffset CreatedAt { get; set; }
}
