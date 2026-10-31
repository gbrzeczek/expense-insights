using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseInsights.Api.Auth;
using ExpenseInsights.Core.Users;
using ExpenseInsights.Testing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace ExpenseInsights.Api.Tests;

public sealed class AuthEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IAsyncDisposable
{
    private static readonly Uri Me = new("/api/me", UriKind.Relative);
    private static readonly Uri Logout = new("/api/auth/logout", UriKind.Relative);

    private readonly ApiFactory _factory = new(postgres);

    [Fact]
    public async Task Me_WithoutASession_IsUnauthorized()
    {
        using var client = _factory.CreateAnonymousClient();

        using var response = await client.GetAsync(Me, CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Owner_SignsInAsTheSeededOwner()
    {
        using var client = await _factory.SignInAsync(ApiFactory.OwnerEmail, CancellationToken);

        var me = await client.GetFromJsonAsync<JsonElement>(Me, CancellationToken);

        Assert.Equal(User.OwnerId, me.GetProperty("id").GetGuid());
        Assert.Equal("owner", me.GetProperty("role").GetString());
        Assert.Equal(ApiFactory.OwnerEmail, me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task InvitedUser_SignsInWithTheUserRole()
    {
        using var client = await _factory.SignInNewUserAsync(CancellationToken);

        var me = await client.GetFromJsonAsync<JsonElement>(Me, CancellationToken);

        Assert.Equal("user", me.GetProperty("role").GetString());
        Assert.NotEqual(User.OwnerId, me.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task UninvitedEmail_IsSentToNoAccess()
    {
        using var client = _factory.CreateAnonymousClient();

        using var login = await client.GetAsync(new Uri("/api/auth/dev-login?email=stranger@example.test", UriKind.Relative), CancellationToken);
        using var me = await client.GetAsync(Me, CancellationToken);

        Assert.Equal("/login?error=noAccess", login.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task SessionCookie_IsHttpOnly_AndOnlyTheXsrfTokenIsReadable()
    {
        var email = $"cookie-{Guid.NewGuid():N}@example.test";
        await Postgres.InviteAsync(email, CancellationToken);
        using var client = _factory.CreateAnonymousClient();

        using var login = await client.GetAsync(new Uri($"/api/auth/dev-login?email={email}", UriKind.Relative), CancellationToken);
        using var me = await client.GetAsync(Me, CancellationToken);

        var session = ApiFactory.SetCookie(login, ".ExpenseInsights.Session");
        Assert.NotNull(session);
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", session, StringComparison.OrdinalIgnoreCase);

        var antiforgery = ApiFactory.SetCookie(me, ".ExpenseInsights.Antiforgery");
        Assert.Contains("httponly", antiforgery, StringComparison.OrdinalIgnoreCase);
        var xsrf = ApiFactory.SetCookie(me, "XSRF-TOKEN");
        Assert.DoesNotContain("httponly", xsrf, StringComparison.OrdinalIgnoreCase);

        // Nothing token-like in what the SPA can read.
        var body = await me.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OutsideDevelopment_CookiesAreSecureOnly()
    {
        using var production = Production();

        var cookie = production.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, cookie.Cookie.SameSite);
    }

    [Fact]
    public async Task DevLogin_DoesNotExistOutsideDevelopment()
    {
        await Postgres.InviteAsync("dev-only@example.test", CancellationToken);
        using var production = Production();
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/api/auth/dev-login?email=dev-only@example.test", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(ApiFactory.SetCookie(response, ".ExpenseInsights.Session"));
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        using var client = await _factory.SignInNewUserAsync(CancellationToken);

        using var logout = await client.PostAsync(Logout, null, CancellationToken);
        using var me = await client.GetAsync(Me, CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task StateChangingRequests_NeedTheXsrfHeader()
    {
        using var client = await _factory.SignInNewUserAsync(CancellationToken);
        var token = client.DefaultRequestHeaders.GetValues("X-XSRF-TOKEN").Single();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using var missing = await client.PostAsync(Logout, null, CancellationToken);
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token[..^4] + "AAAA");
        using var wrong = await client.DeleteAsync(Me, CancellationToken);
        using var me = await client.GetAsync(Me, CancellationToken);

        foreach (var response in new[] { missing, wrong })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("antiforgery", (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task XsrfToken_OfAnotherUser_IsRejected()
    {
        using var first = await _factory.SignInNewUserAsync(CancellationToken);
        using var second = await _factory.SignInNewUserAsync(CancellationToken);
        second.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        second.DefaultRequestHeaders.Add("X-XSRF-TOKEN", first.DefaultRequestHeaders.GetValues("X-XSRF-TOKEN").Single());

        using var response = await second.PostAsync(Logout, null, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_RemovesTheUser_AndEndsEverySession()
    {
        var email = $"leaving-{Guid.NewGuid():N}@example.test";
        await Postgres.InviteAsync(email, CancellationToken);
        using var phone = await _factory.SignInAsync(email, CancellationToken);
        using var laptop = await _factory.SignInAsync(email, CancellationToken);
        var id = (await phone.GetFromJsonAsync<JsonElement>(Me, CancellationToken)).GetProperty("id").GetGuid();

        using var deleted = await phone.DeleteAsync(Me, CancellationToken);
        using var onLaptop = await laptop.GetAsync(Me, CancellationToken);
        using var again = await _factory.CreateAnonymousClient().GetAsync(new Uri($"/api/auth/dev-login?email={email}", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, onLaptop.StatusCode);
        // The invitation went with the account: coming back needs a new one.
        Assert.Equal("/login?error=noAccess", again.Headers.Location?.OriginalString);
        await using var db = CreateDbContext();
        Assert.False(await db.Users.AnyAsync(u => u.Id == id, CancellationToken));
    }

    [Fact]
    public async Task TheOwnersAccount_CantBeDeleted()
    {
        using var owner = await _factory.SignInAsync(ApiFactory.OwnerEmail, CancellationToken);

        using var response = await owner.DeleteAsync(Me, CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ownerCannotBeDeleted", (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task GoogleLogin_RedirectsToGoogle_WithOurCallback()
    {
        using var factory = WithGoogle(_factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/api/auth/login/google?returnUrl=/import", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("test-client.apps.googleusercontent.com", query["client_id"]);
        Assert.Equal("http://localhost/api/auth/callback/google", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        // Query is the default for the code flow, so it isn't sent (form_post would be).
        Assert.False(query.ContainsKey("response_mode"));
        Assert.Equal("openid email profile", query["scope"]);
        Assert.Equal("select_account", query["prompt"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.All(response.Headers.GetValues("Set-Cookie"), c => Assert.Contains("samesite=lax", c, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GoogleLogin_BehindTheTlsProxy_UsesHttpsAndSecureCookies()
    {
        // Production: Caddy terminates TLS and sets X-Forwarded-Proto; docker-compose.prod.yml sets
        // ASPNETCORE_FORWARDEDHEADERS_ENABLED, which reaches configuration as this key.
        using var factory = WithGoogle(Production().WithWebHostBuilder(builder => builder.UseSetting("FORWARDEDHEADERS_ENABLED", "true")));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/login/google", UriKind.Relative));
        request.Headers.Host = "expenses.example.com";
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "100.64.0.7");

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal("https://expenses.example.com/api/auth/callback/google", query["redirect_uri"]);
        Assert.All(response.Headers.GetValues("Set-Cookie"), c => Assert.Contains("secure", c, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GoogleLogin_WithoutCredentials_SaysLoginIsUnavailable()
    {
        using var client = _factory.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri("/api/auth/login/google", UriKind.Relative), CancellationToken);

        Assert.Equal("/login?error=unavailable", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/import", "/import")]
    [InlineData("/transactions?month=2026-10", "/transactions?month=2026-10")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("import", "/")]
    public void ReturnUrl_StaysOnThisSite(string? returnUrl, string expected) =>
        Assert.Equal(expected, AuthEndpoints.LocalUrl(returnUrl));

    private WebApplicationFactory<Program> Production() => _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

    private static WebApplicationFactory<Program> WithGoogle(WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:Google:ClientId", "test-client.apps.googleusercontent.com");
            builder.UseSetting("Auth:Google:ClientSecret", "test-secret");
            // No discovery request to Google from a test.
            builder.ConfigureTestServices(services => services.PostConfigure<OpenIdConnectOptions>(AuthSetup.GoogleScheme, options =>
                options.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = "https://accounts.google.com",
                    AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                }));
        });

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
