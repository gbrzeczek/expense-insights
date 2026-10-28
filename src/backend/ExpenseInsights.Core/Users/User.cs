namespace ExpenseInsights.Core.Users;

/// <summary>An app user. Sign-in (Google) and invitations arrive with plan 03.</summary>
public sealed class User
{
    /// <summary>
    /// The owner, seeded by the initial migration. Until plan 03 adds sign-in, every request runs
    /// as this user.
    /// </summary>
    public static readonly Guid OwnerId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
