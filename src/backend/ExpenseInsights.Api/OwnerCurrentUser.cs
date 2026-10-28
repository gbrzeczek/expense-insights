using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Api;

/// <summary>
/// Until sign-in arrives (plan 03), every request acts as the seeded owner. Plan 03 replaces this
/// with the signed-in user.
/// </summary>
internal sealed class OwnerCurrentUser : ICurrentUser
{
    public Guid UserId => User.OwnerId;
}
