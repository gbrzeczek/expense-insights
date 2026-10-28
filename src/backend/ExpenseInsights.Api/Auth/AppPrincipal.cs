using System.Security.Claims;
using ExpenseInsights.Core.Users;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ExpenseInsights.Api.Auth;

/// <summary>
/// What the session cookie holds: only the internal user id and the role. No provider tokens, so
/// nothing that grants access elsewhere is ever stored.
/// </summary>
internal static class AppPrincipal
{
    public static ClaimsPrincipal Create(User user) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        ],
        CookieAuthenticationDefaults.AuthenticationScheme));

    public static Guid? UserId(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
        && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;
}
