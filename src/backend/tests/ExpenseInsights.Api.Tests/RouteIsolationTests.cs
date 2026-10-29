using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseInsights.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseInsights.Api.Tests;

/// <summary>
/// Hard rule 7 through the HTTP API: for every route, user B can't read, change or delete user A's
/// data, and nobody gets in without a session. A new route fails
/// <see cref="EveryApiRoute_HasAnIsolationCase"/> until it gets a case here.
/// </summary>
public sealed class RouteIsolationTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IAsyncDisposable
{
    private static readonly Route[] Routes =
    [
        new("GET /api/me", "/api/me", async s =>
        {
            var me = await s.B.GetFromJsonAsync<JsonElement>(new Uri("/api/me", UriKind.Relative), CancellationToken);
            Assert.Equal(s.BId, me.GetProperty("id").GetGuid());
        }),
        new("DELETE /api/me", "/api/me", async s =>
        {
            // Deletes B, and only B.
            using var response = await s.B.DeleteAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }),
        new("GET /api/imports", "/api/imports", async s =>
        {
            var batches = await s.B.GetFromJsonAsync<JsonElement>(new Uri("/api/imports", UriKind.Relative), CancellationToken);
            Assert.Empty(batches.EnumerateArray());
        }),
        new("POST /api/imports", "/api/imports", async s =>
        {
            // The same file is a new import for B, not a view of A's.
            using var response = await UploadAsync(s.B);
            var batch = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.False(batch.GetProperty("alreadyImported").GetBoolean());
            Assert.NotEqual(s.ABatchId, batch.GetProperty("id").GetGuid());
        }),
        new("GET /api/invitations", "/api/invitations", s => AssertForbiddenAsync(s.B.GetAsync(new Uri("/api/invitations", UriKind.Relative), CancellationToken))),
        new("POST /api/invitations", "/api/invitations", s => AssertForbiddenAsync(s.B.PostAsJsonAsync(new Uri("/api/invitations", UriKind.Relative), new { email = "x@example.test" }, CancellationToken))),
        new("DELETE /api/invitations/{id:guid}", $"/api/invitations/{Guid.Empty}", s => AssertForbiddenAsync(s.B.DeleteAsync(new Uri($"/api/invitations/{s.AInvitationId}", UriKind.Relative), CancellationToken))),
        new("POST /api/auth/logout", "/api/auth/logout", async s =>
        {
            using var response = await s.B.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null, CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }),
        // Anonymous navigations that start a sign-in; they touch no user data.
        new("GET /api/auth/login/google", "/api/auth/login/google", Anonymous: true, UserBTries: async s =>
        {
            using var response = await s.B.GetAsync(new Uri("/api/auth/login/google", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }),
        // Development only (see AuthEndpointTests): it signs in by email by design.
        new("GET /api/auth/dev-login", "/api/auth/dev-login", Anonymous: true, UserBTries: async s =>
        {
            using var response = await s.B.GetAsync(new Uri($"/api/auth/dev-login?email={s.BEmail}", UriKind.Relative), CancellationToken);
            Assert.Equal("/", response.Headers.Location?.OriginalString);
        }),
    ];

    private readonly ApiFactory _factory = new(postgres);

    public static TheoryData<string> RouteKeys => [.. Routes.Select(r => r.Key)];

    public static TheoryData<string> ProtectedRouteKeys => [.. Routes.Where(r => !r.Anonymous).Select(r => r.Key)];

    [Fact]
    public void EveryApiRoute_HasAnIsolationCase()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            // The /api fallback (404 problem details) has no methods and serves no data.
            .Where(e => !e.RoutePattern.RawText!.Contains("{*", StringComparison.Ordinal))
            .SelectMany(e => e.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods.Select(m => $"{m} {e.RoutePattern.RawText!.TrimEnd('/')}"))
            .Order()
            .ToList();

        Assert.Equal(Routes.Select(r => r.Key).Order(), endpoints);
    }

    [Theory]
    [MemberData(nameof(RouteKeys))]
    public async Task UserB_CantReachUserAsData(string key)
    {
        var scenario = await ArrangeAsync();
        var before = await SnapshotAsync(scenario);

        await Routes.Single(r => r.Key == key).UserBTries(scenario);

        Assert.Equal(before, await SnapshotAsync(scenario));
        var me = await scenario.A.GetFromJsonAsync<JsonElement>(new Uri("/api/me", UriKind.Relative), CancellationToken);
        Assert.Equal(scenario.AId, me.GetProperty("id").GetGuid());
        var batches = await scenario.A.GetFromJsonAsync<JsonElement>(new Uri("/api/imports", UriKind.Relative), CancellationToken);
        Assert.Equal(scenario.ABatchId, Assert.Single(batches.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Theory]
    [MemberData(nameof(ProtectedRouteKeys))]
    public async Task WithoutASession_EveryRouteIsUnauthorized(string key)
    {
        var route = Routes.Single(r => r.Key == key);
        using var client = _factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(new HttpMethod(key.Split(' ')[0]), new Uri(route.Path, UriKind.Relative));

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Scenario> ArrangeAsync()
    {
        var aEmail = $"a-{Guid.NewGuid():N}@example.test";
        var bEmail = $"b-{Guid.NewGuid():N}@example.test";
        var aInvitation = await Postgres.InviteAsync(aEmail, CancellationToken);
        await Postgres.InviteAsync(bEmail, CancellationToken);
        var a = await _factory.SignInAsync(aEmail, CancellationToken);
        var b = await _factory.SignInAsync(bEmail, CancellationToken);
        using var upload = await UploadAsync(a);
        var batch = await upload.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return new Scenario(a, b, await IdAsync(a), await IdAsync(b), bEmail, batch.GetProperty("id").GetGuid(), aInvitation);
    }

    /// <summary>Everything A owns, read straight from the database as A.</summary>
    private async Task<(int Accounts, int Transactions, int Batches, decimal Sum)> SnapshotAsync(Scenario s)
    {
        await using var db = CreateDbContext(s.AId);
        return (
            await db.Accounts.CountAsync(CancellationToken),
            await db.Transactions.CountAsync(CancellationToken),
            await db.ImportBatches.CountAsync(CancellationToken),
            await db.Transactions.SumAsync(t => t.Amount, CancellationToken));
    }

    private static async Task<Guid> IdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>(new Uri("/api/me", UriKind.Relative), CancellationToken)).GetProperty("id").GetGuid();

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client)
    {
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(TestData.Read("fake_ing.csv"));
        part.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(part, "file", "fake_ing.csv");
        var response = await client.PostAsync(new Uri("/api/imports", UriKind.Relative), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private static async Task AssertForbiddenAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    /// <param name="Key">The route as <c>METHOD pattern</c>, as the endpoint data source lists it.</param>
    /// <param name="Path">A concrete path for the anonymous check.</param>
    /// <param name="UserBTries">User B uses the route against A's data; A's data must be unchanged after.</param>
    /// <param name="Anonymous">The route works without a session (it starts one).</param>
    private sealed record Route(string Key, string Path, Func<Scenario, Task> UserBTries, bool Anonymous = false);

    private sealed record Scenario(HttpClient A, HttpClient B, Guid AId, Guid BId, string BEmail, Guid ABatchId, Guid AInvitationId);
}
