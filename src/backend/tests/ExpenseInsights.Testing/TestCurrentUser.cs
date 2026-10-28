using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Testing;

public sealed class TestCurrentUser(Guid userId) : ICurrentUser
{
    public Guid UserId { get; } = userId;
}
