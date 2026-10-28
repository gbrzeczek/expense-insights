namespace ExpenseInsights.Core.Users;

/// <summary>An entity that belongs to one user; queries are filtered to the current user.</summary>
public interface IUserOwned
{
    Guid UserId { get; set; }
}
