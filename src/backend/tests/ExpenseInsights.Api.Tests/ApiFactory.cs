using System.Net;
using ExpenseInsights.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExpenseInsights.Api.Tests;

/// <summary>
/// The API in Development (the only environment with the fake dev login), on a test database. Clients
/// keep cookies and don't follow redirects, like a browser tab the test can inspect.
/// </summary>
public class ApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    public const string OwnerEmail = "owner@example.test";

    protected PostgresFixture Postgres { get; } = postgres;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", Postgres.ConnectionString);
        builder.UseSetting("Auth:OwnerEmail", OwnerEmail);
        // Not from a developer's user-secrets.
        builder.UseSetting("Auth:Google:ClientId", "");
        builder.UseSetting("Auth:Google:ClientSecret", "");
    }

    public HttpClient CreateAnonymousClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Signs in through the dev login and sends the XSRF header, as the Angular app does.</summary>
    public async Task<HttpClient> SignInAsync(string email, CancellationToken ct)
    {
        var client = CreateAnonymousClient();
        using var login = await client.GetAsync(new Uri($"/api/auth/dev-login?email={Uri.EscapeDataString(email)}&returnUrl=/", UriKind.Relative), ct);
        if (login.StatusCode != HttpStatusCode.Redirect || login.Headers.Location?.OriginalString != "/")
        {
            throw new InvalidOperationException($"Signing in failed: {login.StatusCode} {login.Headers.Location}");
        }

        using var me = await client.GetAsync(new Uri("/api/me", UriKind.Relative), ct);
        me.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", XsrfToken(me));
        return client;
    }

    /// <summary>A fresh invited user, so tests sharing the database don't see each other's data.</summary>
    public async Task<HttpClient> SignInNewUserAsync(CancellationToken ct)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        await Postgres.InviteAsync(email, ct);
        return await SignInAsync(email, ct);
    }

    public static string XsrfToken(HttpResponseMessage response) =>
        Uri.UnescapeDataString(SetCookie(response, "XSRF-TOKEN")?.Split(';')[0].Split('=', 2)[1]
            ?? throw new InvalidOperationException("No XSRF-TOKEN cookie was issued."));

    public static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal))
            : null;
}
