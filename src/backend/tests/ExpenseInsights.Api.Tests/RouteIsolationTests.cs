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
        new("GET /api/transactions", "/api/transactions", async s =>
        {
            var page = await s.B.GetFromJsonAsync<JsonElement>(new Uri("/api/transactions", UriKind.Relative), CancellationToken);
            Assert.Empty(page.GetProperty("items").EnumerateArray());
        }),
        new("GET /api/transactions/{id:guid}", $"/api/transactions/{Guid.Empty}", s => AssertNotFoundAsync(s.B.GetAsync(new Uri($"/api/transactions/{s.ATransactionId}", UriKind.Relative), CancellationToken))),
        new("PUT /api/transactions/{id:guid}/category", $"/api/transactions/{Guid.Empty}/category", s => AssertNotFoundAsync(s.B.PutAsJsonAsync(new Uri($"/api/transactions/{s.ATransactionId}/category", UriKind.Relative), new { categoryId = OtherCategory }, CancellationToken))),
        new("POST /api/transactions/{id:guid}/apply-to-all", $"/api/transactions/{Guid.Empty}/apply-to-all", s => AssertNotFoundAsync(s.B.PostAsync(new Uri($"/api/transactions/{s.ATransactionId}/apply-to-all", UriKind.Relative), null, CancellationToken))),
        new("PUT /api/transactions/{id:guid}/offsets", $"/api/transactions/{Guid.Empty}/offsets", s => AssertNotFoundAsync(s.B.PutAsJsonAsync(new Uri($"/api/transactions/{s.ATransactionId}/offsets", UriKind.Relative), new { categoryId = (Guid?)null }, CancellationToken))),
        new("GET /api/categories", "/api/categories", async s =>
        {
            var categories = await s.B.GetFromJsonAsync<JsonElement>(new Uri("/api/categories", UriKind.Relative), CancellationToken);
            Assert.DoesNotContain(categories.EnumerateArray(), c => c.GetProperty("id").GetGuid() == s.ACategoryId);
            Assert.All(categories.EnumerateArray(), c => Assert.False(c.GetProperty("hidden").GetBoolean()));
        }),
        new("POST /api/categories", "/api/categories", async s =>
        {
            // A's own category can't be B's parent.
            using var response = await s.B.PostAsJsonAsync(new Uri("/api/categories", UriKind.Relative), new { parentId = s.ACategoryId, name = "Herbata" }, CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }),
        new("PUT /api/categories/{id:guid}", $"/api/categories/{Guid.Empty}", async s =>
        {
            await AssertNotFoundAsync(s.B.PutAsJsonAsync(new Uri($"/api/categories/{s.ACategoryId}", UriKind.Relative), new { name = "Przejęta", hidden = true }, CancellationToken));
            // Hiding a global category is B's own setting, not A's.
            using var response = await s.B.PutAsJsonAsync(new Uri($"/api/categories/{OtherCategory}", UriKind.Relative), new { name = "Różne", hidden = true }, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }),
        new("GET /api/merchants/to-review", "/api/merchants/to-review", async s =>
        {
            var review = await s.B.GetFromJsonAsync<JsonElement>(new Uri("/api/merchants/to-review", UriKind.Relative), CancellationToken);
            Assert.Empty(review.GetProperty("groups").EnumerateArray());
            Assert.Empty(review.GetProperty("progress").EnumerateArray());
        }),
        new("POST /api/merchants/category", "/api/merchants/category", s => AssertNotFoundAsync(s.B.PostAsJsonAsync(new Uri("/api/merchants/category", UriKind.Relative), new { merchantKey = "biedronka", categoryId = OtherCategory }, CancellationToken))),
        new("GET /api/rules", "/api/rules", async s =>
        {
            var rules = await s.B.GetFromJsonAsync<JsonElement>(new Uri("/api/rules", UriKind.Relative), CancellationToken);
            Assert.Empty(rules.EnumerateArray());
        }),
        new("DELETE /api/rules/{id:guid}", $"/api/rules/{Guid.Empty}", s => AssertNotFoundAsync(s.B.DeleteAsync(new Uri($"/api/rules/{s.ARuleId}", UriKind.Relative), CancellationToken))),
        new("POST /api/classification/run", "/api/classification/run", async s =>
        {
            using var response = await s.B.PostAsync(new Uri("/api/classification/run", UriKind.Relative), null, CancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }),
        new("GET /api/classification/labels", "/api/classification/labels", async s =>
        {
            var csv = await s.B.GetStringAsync(new Uri("/api/classification/labels", UriKind.Relative), CancellationToken);
            Assert.Equal("tx_id;tx_date;amount;category\n", csv);
        }),
        new("GET /api/insights/overview", "/api/insights/overview", async s =>
        {
            var overview = await s.B.GetFromJsonAsync<JsonElement>(new Uri("/api/insights/overview", UriKind.Relative), CancellationToken);
            Assert.False(overview.GetProperty("hasData").GetBoolean());
            Assert.Equal(0m, overview.GetProperty("spent").GetDecimal());
        }),
        new("GET /api/insights/categories/{id:guid}", $"/api/insights/categories/{OtherCategory}", async s =>
        {
            // A's own subcategory doesn't exist for B; a global one has none of A's spending.
            await AssertNotFoundAsync(s.B.GetAsync(new Uri($"/api/insights/categories/{s.ACategoryId}", UriKind.Relative), CancellationToken));
            var other = await s.B.GetFromJsonAsync<JsonElement>(new Uri($"/api/insights/categories/{OtherCategory}", UriKind.Relative), CancellationToken);
            Assert.Equal(0m, other.GetProperty("spent").GetDecimal());
            Assert.Empty(other.GetProperty("merchants").EnumerateArray());
        }),
        new("GET /api/insights/transactions", "/api/insights/transactions", async s =>
        {
            var transactions = await s.B.GetFromJsonAsync<JsonElement>(new Uri("/api/insights/transactions?merchant=zabka", UriKind.Relative), CancellationToken);
            Assert.Empty(transactions.GetProperty("items").EnumerateArray());
        }),
        new("GET /api/insights/subscriptions", "/api/insights/subscriptions", async s =>
        {
            var subscriptions = await s.B.GetFromJsonAsync<JsonElement>(new Uri("/api/insights/subscriptions", UriKind.Relative), CancellationToken);
            Assert.Empty(subscriptions.GetProperty("items").EnumerateArray());
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

    /// <summary>The global "Inne" category.</summary>
    private static readonly Guid OtherCategory = new("0000000c-0000-0000-0000-000000000014");

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
        var aId = await IdAsync(a);
        await WaitForClassificationAsync(aId);

        // A's own category and rule, and one of A's transactions.
        using var category = await a.PostAsJsonAsync(new Uri("/api/categories", UriKind.Relative), new { parentId = new Guid("0000000c-0000-0000-0000-000000000002"), name = "Kawa" }, CancellationToken);
        using var label = await a.PostAsJsonAsync(new Uri("/api/merchants/category", UriKind.Relative), new { merchantKey = "zabka", categoryId = OtherCategory }, CancellationToken);
        var page = await a.GetFromJsonAsync<JsonElement>(new Uri("/api/transactions?limit=1", UriKind.Relative), CancellationToken);
        return new Scenario(
            a, b, aId, await IdAsync(b), bEmail, batch.GetProperty("id").GetGuid(), aInvitation,
            page.GetProperty("items")[0].GetProperty("id").GetGuid(),
            (await category.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid(),
            (await label.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("rule").GetProperty("id").GetGuid());
    }

    /// <summary>The import is classified in the background; wait so it doesn't change A's data mid-test.</summary>
    private async Task WaitForClassificationAsync(Guid userId)
    {
        for (var i = 0; i < 100; i++)
        {
            await using var db = CreateDbContext(userId);
            if (!await db.Transactions.AnyAsync(t => t.ClassifiedAt == null, CancellationToken))
            {
                return;
            }

            await Task.Delay(100, CancellationToken);
        }

        Assert.Fail("The import wasn't classified in time.");
    }

    /// <summary>Everything A owns, read straight from the database as A.</summary>
    private async Task<(int Accounts, int Transactions, int Batches, decimal Sum, string Classification, string Rules, string Categories)> SnapshotAsync(Scenario s)
    {
        await using var db = CreateDbContext(s.AId);
        var classification = await db.Transactions.OrderBy(t => t.Id)
            .Select(t => $"{t.Id}:{t.CategoryId}:{t.ClassificationSource}:{t.OffsetsCategoryId}")
            .ToListAsync(CancellationToken);
        var rules = await db.MerchantRules.OrderBy(r => r.Id).Select(r => $"{r.Id}:{r.Pattern}:{r.CategoryId}").ToListAsync(CancellationToken);
        var categories = await db.Categories.Where(c => c.UserId != null).OrderBy(c => c.Id).Select(c => $"{c.Id}:{c.Name}:{c.IsHidden}").ToListAsync(CancellationToken);
        var settings = await db.CategorySettings.OrderBy(c => c.Id).Select(c => $"{c.CategoryId}:{c.Name}:{c.IsHidden}").ToListAsync(CancellationToken);
        return (
            await db.Accounts.CountAsync(CancellationToken),
            await db.Transactions.CountAsync(CancellationToken),
            await db.ImportBatches.CountAsync(CancellationToken),
            await db.Transactions.SumAsync(t => t.Amount, CancellationToken),
            string.Join(',', classification),
            string.Join(',', rules),
            string.Join(',', categories.Concat(settings)));
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

    private static async Task AssertNotFoundAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    private sealed record Scenario(
        HttpClient A, HttpClient B, Guid AId, Guid BId, string BEmail, Guid ABatchId, Guid AInvitationId, Guid ATransactionId, Guid ACategoryId, Guid ARuleId);
}
