using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Api.Auth;

/// <summary>
/// The signed-in user of the current request. Without one (anonymous requests, startup) the id is
/// <see cref="Guid.Empty"/>: user-owned queries then match nothing and writes are refused.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId => AppPrincipal.UserId(accessor.HttpContext?.User) ?? Guid.Empty;
}
