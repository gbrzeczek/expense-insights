namespace ExpenseInsights.Core.Users;

public interface ICurrentUser
{
    Guid UserId { get; }
}
