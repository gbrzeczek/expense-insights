using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ExpenseInsights.Api.Auth;

internal static class AuthEndpoints
{
    /// <summary>The SPA's login page, optionally with an error code it words through i18n.</summary>
    public static string LoginPage(string? error = null) => error is null ? "/login" : $"/login?error={error}";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder api, IWebHostEnvironment environment)
    {
        var auth = api.MapGroup("/auth");

        // Top-level navigations (not XHR): they end in a redirect back into the SPA.
        auth.MapGet("/login/google", LoginWithGoogleAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync);

        if (environment.IsDevelopment())
        {
            auth.MapGet("/dev-login", DevLoginAsync).AllowAnonymous();
        }
    }

    private static async Task<IResult> LoginWithGoogleAsync(string? returnUrl, AuthOptions options, IAuthenticationSchemeProvider schemes)
    {
        if (!options.Google.IsConfigured || await schemes.GetSchemeAsync(AuthSetup.GoogleScheme) is null)
        {
            return Results.Redirect(LoginPage("unavailable"));
        }

        return Results.Challenge(new AuthenticationProperties { RedirectUri = LocalUrl(returnUrl) }, [AuthSetup.GoogleScheme]);
    }

    private static async Task LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Xsrf.Clear(context);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    /// <summary>
    /// Development only: signs in as an invited email without Google, through the same allow-list
    /// (for e2e tests and local work without OAuth credentials).
    /// </summary>
    private static async Task<IResult> DevLoginAsync(string email, string? returnUrl, HttpContext context, UserSignIn signIn, CancellationToken ct)
    {
        var identity = new ExternalIdentity(ExternalLogin.Dev, Invitation.NormalizeEmail(email), email, EmailVerified: true, DisplayName: null);
        var user = await signIn.SignInAsync(identity, ct);
        if (user is null)
        {
            return Results.Redirect(LoginPage("noAccess"));
        }

        await AuthSetup.SignInAsync(context, user);
        return Results.Redirect(LocalUrl(returnUrl));
    }

    /// <summary>Only same-site paths, so a crafted link can't bounce a fresh session to another site.</summary>
    internal static string LocalUrl(string? url) =>
        url is ['/', ..] && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : "/";
}
