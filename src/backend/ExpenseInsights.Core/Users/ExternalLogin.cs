namespace ExpenseInsights.Core.Users;

/// <summary>
/// Links a user to an identity provider's account: Google now, Apple later. Not filtered by user:
/// sign-in looks it up before anyone is signed in.
/// </summary>
public sealed class ExternalLogin
{
    public const string Google = "google";

    /// <summary>The Development-only fake login (e2e and local testing).</summary>
    public const string Dev = "dev";

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Provider { get; set; }

    /// <summary>The provider's stable account id (the OIDC <c>sub</c> claim), never the email.</summary>
    public required string Subject { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSignInAt { get; set; }
}
