using System.Security.Claims;
using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Persistence;
using ExpenseInsights.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api.Auth;

/// <summary>Configuration under <c>Auth</c>. The Google client id and secret come from user-secrets or env vars.</summary>
internal sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>Seeds the allow-list; this address signs in as the owner.</summary>
    public string? OwnerEmail { get; set; }

    public GoogleOptions Google { get; set; } = new();

    internal sealed class GoogleOptions
    {
        public string? ClientId { get; set; }

        public string? ClientSecret { get; set; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
    }
}

/// <summary>
/// Google OIDC with a cookie session: the Google handler only proves who someone is, then
/// <see cref="UserSignIn"/> decides whether they get in and the cookie holds our own user id.
/// </summary>
internal static class AuthSetup
{
    public const string GoogleScheme = "google";
    public const string OwnerPolicy = "owner";
    public const string GoogleCallbackPath = "/api/auth/callback/google";

    public static void AddAppAuth(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new();
        builder.Services.AddSingleton(options);

        // Development serves plain HTTP on localhost (and to a phone on the LAN); everything else is HTTPS.
        var securePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
        builder.Services.AddDataProtection().SetApplicationName("expense-insights").PersistKeysToDbContext<AppDbContext>();

        var auth = builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = ".ExpenseInsights.Session";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = securePolicy;
                cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
                cookie.SlidingExpiration = true;
                cookie.Events = new CookieAuthenticationEvents
                {
                    // An API: no redirects to a login page, the SPA handles 401 and 403 itself.
                    OnRedirectToLogin = context => SetStatus(context.Response, StatusCodes.Status401Unauthorized),
                    OnRedirectToAccessDenied = context => SetStatus(context.Response, StatusCodes.Status403Forbidden),
                    OnValidatePrincipal = RejectDeletedUsersAsync,
                };
            });

        if (options.Google.IsConfigured)
        {
            auth.AddOpenIdConnect(GoogleScheme, "Google", google =>
            {
                google.Authority = "https://accounts.google.com";
                google.ClientId = options.Google.ClientId;
                google.ClientSecret = options.Google.ClientSecret;
                google.ResponseType = "code";
                // Google redirects back with a GET, so the correlation and nonce cookies can be
                // SameSite=Lax (form_post would need SameSite=None, i.e. Secure, which plain-HTTP
                // localhost can't do). PKCE protects the code in the URL.
                google.ResponseMode = "query";
                google.CallbackPath = GoogleCallbackPath;
                google.Scope.Clear();
                google.Scope.Add("openid");
                google.Scope.Add("email");
                google.Scope.Add("profile");
                google.MapInboundClaims = false;
                google.GetClaimsFromUserInfoEndpoint = false;
                google.SaveTokens = false;
                google.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                google.CorrelationCookie.SecurePolicy = securePolicy;
                google.CorrelationCookie.SameSite = SameSiteMode.Lax;
                google.NonceCookie.SecurePolicy = securePolicy;
                google.NonceCookie.SameSite = SameSiteMode.Lax;
                google.Events = new OpenIdConnectEvents
                {
                    OnRedirectToIdentityProvider = context =>
                    {
                        // Lets someone pick another Google account after "no access".
                        context.ProtocolMessage.Prompt = "select_account";
                        return Task.CompletedTask;
                    },
                    OnTicketReceived = SignInGoogleUserAsync,
                    OnRemoteFailure = context =>
                    {
                        // Cancelled at Google, expired correlation cookie, ...
                        context.Response.Redirect(AuthEndpoints.LoginPage("failed"));
                        context.HandleResponse();
                        return Task.CompletedTask;
                    },
                };
            });
        }

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(OwnerPolicy, policy => policy.RequireRole(nameof(UserRole.Owner)));

        builder.Services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = Xsrf.HeaderName;
            antiforgery.Cookie.Name = ".ExpenseInsights.Antiforgery";
            antiforgery.Cookie.SecurePolicy = securePolicy;
        });
    }

    public static void UseAppAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseXsrf();
        app.UseAntiforgery();
    }

    /// <summary>Signs in a user who passed the allow-list, with our own principal instead of Google's claims.</summary>
    public static async Task SignInAsync(HttpContext context, User user) =>
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AppPrincipal.Create(user),
            new AuthenticationProperties { IsPersistent = true });

    private static async Task SignInGoogleUserAsync(TicketReceivedContext context)
    {
        var google = context.Principal!;
        var identity = new ExternalIdentity(
            ExternalLogin.Google,
            google.FindFirstValue("sub") ?? throw new InvalidOperationException("Google returned no subject."),
            google.FindFirstValue("email"),
            bool.TryParse(google.FindFirstValue("email_verified"), out var verified) && verified,
            google.FindFirstValue("name"));

        var signIn = context.HttpContext.RequestServices.GetRequiredService<UserSignIn>();
        var user = await signIn.SignInAsync(identity, context.HttpContext.RequestAborted);
        if (user is null)
        {
            context.Response.Redirect(AuthEndpoints.LoginPage("noAccess"));
            context.HandleResponse();
            return;
        }

        context.Principal = AppPrincipal.Create(user);
        context.Properties!.IsPersistent = true;
    }

    /// <summary>A deleted user's cookie stops working on every device at once.</summary>
    private static async Task RejectDeletedUsersAsync(CookieValidatePrincipalContext context)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var userId = AppPrincipal.UserId(context.Principal);
        if (userId is null || !await db.Users.AnyAsync(u => u.Id == userId, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private static Task SetStatus(HttpResponse response, int status)
    {
        response.StatusCode = status;
        return Task.CompletedTask;
    }
}
