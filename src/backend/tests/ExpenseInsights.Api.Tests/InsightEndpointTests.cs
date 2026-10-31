using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseInsights.Testing;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api.Tests;

public sealed class InsightEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IAsyncDisposable
{
    private static readonly Guid Groceries = new("0000000c-0000-0000-0000-000000000001");

    private readonly ApiFactory _factory = new(postgres);

    [Fact]
    public async Task BeforeTheFirstImport_TheOverviewIsEmpty()
    {
        var client = await _factory.SignInNewUserAsync(CancellationToken);

        var overview = await GetAsync(client, "/api/insights/overview");
        var subscriptions = await GetAsync(client, "/api/insights/subscriptions");

        Assert.False(overview.GetProperty("hasData").GetBoolean());
        Assert.Empty(overview.GetProperty("insights").EnumerateArray());
        Assert.Empty(subscriptions.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Overview_DefaultsToTheLatestMonth_InProgress()
    {
        var client = await ImportedUserAsync();

        var overview = await GetAsync(client, "/api/insights/overview");

        Assert.True(overview.GetProperty("hasData").GetBoolean());
        Assert.Equal("2026-10", overview.GetProperty("month").GetString());
        Assert.Equal("2026-05", overview.GetProperty("earliestMonth").GetString());
        Assert.Equal("PLN", overview.GetProperty("currency").GetString());
        Assert.True(overview.GetProperty("isPartial").GetBoolean());
        Assert.Equal("2026-10-28", overview.GetProperty("through").GetString());
        Assert.True(overview.GetProperty("usual").GetProperty("byDay").GetBoolean());
        Assert.True(overview.GetProperty("projected").GetDecimal() >= overview.GetProperty("spent").GetDecimal());
        Assert.Equal(6, overview.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task EveryNumber_LeadsToTheTransactionsBehindIt()
    {
        var client = await ImportedUserAsync();

        var overview = await GetAsync(client, "/api/insights/overview?month=2026-09");
        var all = await GetAsync(client, "/api/insights/transactions?month=2026-09");
        var spent = overview.GetProperty("spent").GetDecimal();
        var categories = overview.GetProperty("categories").EnumerateArray().ToList();

        Assert.False(overview.GetProperty("isPartial").GetBoolean());
        Assert.Equal(spent, all.GetProperty("total").GetDecimal());
        Assert.Equal(spent, all.GetProperty("items").EnumerateArray().Sum(i => i.GetProperty("counted").GetDecimal()));
        Assert.Equal(spent, categories.Sum(c => c.GetProperty("amount").GetDecimal()));
        foreach (var category in categories)
        {
            var id = category.GetProperty("categoryId") is { ValueKind: JsonValueKind.String } value ? value.GetString() : "none";
            var behind = await GetAsync(client, $"/api/insights/transactions?month=2026-09&category={id}");
            Assert.Equal(category.GetProperty("amount").GetDecimal(), behind.GetProperty("total").GetDecimal());
        }

        // Category → merchant → transactions.
        var groceries = await GetAsync(client, $"/api/insights/categories/{Groceries}?month=2026-09");
        var top = groceries.GetProperty("merchants")[0];
        var merchant = await GetAsync(client, $"/api/insights/transactions?month=2026-09&category={Groceries}&merchant={top.GetProperty("merchantKey").GetString()}");
        Assert.Equal(top.GetProperty("amount").GetDecimal(), merchant.GetProperty("total").GetDecimal());
        Assert.Equal(top.GetProperty("count").GetInt32(), merchant.GetProperty("items").GetArrayLength());
        Assert.Equal(categories.Single(c => c.GetProperty("categoryId").ValueKind == JsonValueKind.String && c.GetProperty("categoryId").GetGuid() == Groceries).GetProperty("amount").GetDecimal(), groceries.GetProperty("spent").GetDecimal());
        Assert.Equal(5, groceries.GetProperty("trend").GetArrayLength()); // May–September
    }

    [Fact]
    public async Task Subscriptions_ListTheRecurringPayments()
    {
        var client = await ImportedUserAsync();

        var subscriptions = await GetAsync(client, "/api/insights/subscriptions");

        var items = subscriptions.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("merchantKey").GetString() == "netflix" && i.GetProperty("latestAmount").GetDecimal() == 49m);
        Assert.All(items, i => Assert.Equal(30, i.GetProperty("cadenceDays").GetInt32()));
        Assert.Equal(items.Sum(i => i.GetProperty("monthlyCost").GetDecimal()), subscriptions.GetProperty("monthlyTotal").GetDecimal());
    }

    [Theory]
    [InlineData("/api/insights/overview?month=2026-13")]
    [InlineData("/api/insights/overview?month=2020-01")]
    [InlineData("/api/insights/transactions?month=2026-09&category=groceries")]
    public async Task InvalidInput_IsABadRequest(string path)
    {
        var client = await ImportedUserAsync();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Category_MustBeTopLevel()
    {
        var client = await _factory.SignInNewUserAsync(CancellationToken);

        using var response = await client.GetAsync(new Uri($"/api/insights/categories/{Guid.NewGuid()}", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpClient> ImportedUserAsync()
    {
        var client = await _factory.SignInNewUserAsync(CancellationToken);
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(TestData.Read("fake_ing.csv"));
        part.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(part, "file", "fake_ing.csv");
        using var upload = await client.PostAsync(new Uri("/api/imports", UriKind.Relative), content, CancellationToken);
        upload.EnsureSuccessStatusCode();

        // Classified in the background: wait for it, so the numbers don't move mid-test.
        var userId = (await GetAsync(client, "/api/me")).GetProperty("id").GetGuid();
        for (var i = 0; i < 100; i++)
        {
            await using var db = CreateDbContext(userId);
            if (!await db.Transactions.AnyAsync(t => t.ClassifiedAt == null, CancellationToken))
            {
                return client;
            }

            await Task.Delay(100, CancellationToken);
        }

        throw new TimeoutException("The import wasn't classified.");
    }

    private static Task<JsonElement> GetAsync(HttpClient client, string path) =>
        client.GetFromJsonAsync<JsonElement>(new Uri(path, UriKind.Relative), CancellationToken);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
