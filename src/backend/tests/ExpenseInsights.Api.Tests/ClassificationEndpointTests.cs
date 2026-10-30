using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseInsights.Testing;

namespace ExpenseInsights.Api.Tests;

public sealed class ClassificationEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IAsyncDisposable
{
    private static readonly Guid Groceries = new("0000000c-0000-0000-0000-000000000001");
    private static readonly Guid Restaurants = new("0000000c-0000-0000-0000-000000000002");
    private static readonly Guid Health = new("0000000c-0000-0000-0000-000000000004");
    private static readonly Guid Other = new("0000000c-0000-0000-0000-000000000014");

    private readonly ApiFactory _factory = new(postgres);

    [Fact]
    public async Task AfterImport_TransactionsAreClassifiedInTheBackground()
    {
        var client = await ImportedUserAsync();

        var first = await GetAsync(client, "/api/transactions?limit=500");
        var rest = await GetAsync(client, $"/api/transactions?limit=200&offset={first.GetProperty("nextOffset").GetInt32()}");

        var items = first.GetProperty("items").EnumerateArray().Concat(rest.GetProperty("items").EnumerateArray()).ToList();
        Assert.Equal(200, first.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("nextOffset").ValueKind);
        Assert.Equal(335, items.Count);
        Assert.True(items.Count(i => i.GetProperty("categoryId").ValueKind != JsonValueKind.Null) >= 300);
        var pyszne = items.First(i => i.GetProperty("merchantKey").GetString() == "pyszne");
        Assert.Equal("pyszne.pl", pyszne.GetProperty("merchant").GetString());
        Assert.Equal("globalRule", pyszne.GetProperty("source").GetString());
    }

    [Fact]
    public async Task List_PagesNewestFirst_FiltersAndSearches()
    {
        var client = await ImportedUserAsync();

        var first = await GetAsync(client, "/api/transactions?limit=20");
        var second = await GetAsync(client, $"/api/transactions?limit=20&offset={first.GetProperty("nextOffset").GetInt32()}");
        var unclassified = await GetAsync(client, "/api/transactions?filter=unclassified&limit=200");
        var search = await GetAsync(client, "/api/transactions?q=bIeDrOnKa&limit=200");
        var byAmount = await GetAsync(client, "/api/transactions?q=742,18");
        var byCategory = await GetAsync(client, $"/api/transactions?category={Health}&limit=200");

        var dates = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("date").GetString()).ToList();
        Assert.Equal(40, dates.Count);
        Assert.Equal(dates.OrderDescending(StringComparer.Ordinal), dates);
        Assert.All(unclassified.GetProperty("items").EnumerateArray(), i => Assert.Equal(JsonValueKind.Null, i.GetProperty("categoryId").ValueKind));
        Assert.NotEmpty(unclassified.GetProperty("items").EnumerateArray());
        Assert.All(search.GetProperty("items").EnumerateArray(), i => Assert.Equal("biedronka", i.GetProperty("merchantKey").GetString()));
        Assert.Equal(6, byAmount.GetProperty("items").GetArrayLength());
        Assert.All(byCategory.GetProperty("items").EnumerateArray(), i => Assert.Equal(Health, i.GetProperty("categoryId").GetGuid()));
        using var invalid = await client.GetAsync(new Uri("/api/transactions?filter=nope", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Detail_SuggestsThreeOtherCategories_AndMasksTheAccount()
    {
        var client = await ImportedUserAsync();
        var transfer = await FirstAsync(client, "tauron sprzedaz");

        var detail = await GetAsync(client, $"/api/transactions/{transfer}");

        var suggestions = detail.GetProperty("suggestions").EnumerateArray().Select(s => s.GetGuid()).ToList();
        Assert.Equal(3, suggestions.Count);
        Assert.DoesNotContain(detail.GetProperty("transaction").GetProperty("categoryId").GetGuid(), suggestions);
        Assert.StartsWith("•••• ", detail.GetProperty("counterpartyAccount").GetString(), StringComparison.Ordinal);
        Assert.Contains(detail.GetProperty("raw").EnumerateArray(), f => f.GetString()!.StartsWith("•••• ", StringComparison.Ordinal));
        Assert.Equal("KONTO Mobi pow.26", detail.GetProperty("accountName").GetString());
    }

    [Fact]
    public async Task CorrectThenApplyToAll_RecategorizesTheMerchant()
    {
        var client = await ImportedUserAsync();
        var rossmann = await FirstAsync(client, "rossmann");

        using var correct = await client.PutAsJsonAsync(new Uri($"/api/transactions/{rossmann}/category", UriKind.Relative), new { categoryId = Health }, CancellationToken);
        var correction = await correct.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        using var apply = await client.PostAsync(new Uri($"/api/transactions/{rossmann}/apply-to-all", UriKind.Relative), null, CancellationToken);
        var applied = await apply.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        Assert.Equal("userCorrection", correction.GetProperty("transaction").GetProperty("source").GetString());
        var offer = correction.GetProperty("applyToAll");
        Assert.Equal("rossmann", offer.GetProperty("label").GetString());
        Assert.Equal(offer.GetProperty("count").GetInt32(), applied.GetProperty("changed").GetInt32());
        Assert.True(applied.GetProperty("changed").GetInt32() > 0);
        var search = await GetAsync(client, "/api/transactions?q=rossmann&limit=100");
        Assert.All(search.GetProperty("items").EnumerateArray(), i => Assert.Equal(Health, i.GetProperty("categoryId").GetGuid()));
        var rules = await GetAsync(client, "/api/rules");
        Assert.Equal("rossmann", Assert.Single(rules.EnumerateArray()).GetProperty("pattern").GetString());
    }

    [Fact]
    public async Task ACorrectionToAnUnknownCategory_IsRejected()
    {
        var client = await ImportedUserAsync();
        var lidl = await FirstAsync(client, "lidl kamienskiego");

        using var response = await client.PutAsJsonAsync(new Uri($"/api/transactions/{lidl}/category", UriKind.Relative), new { categoryId = Guid.NewGuid() }, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("categoryNotFound", (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Onboarding_ListsOpenMerchants_AndLabellingOneRaisesTheProgress()
    {
        var client = await ImportedUserAsync();

        var before = await GetAsync(client, "/api/merchants/to-review");
        var group = before.GetProperty("groups").EnumerateArray().First(g => g.GetProperty("merchantKey").GetString() == "krakow zakopian");
        using var label = await client.PostAsJsonAsync(new Uri("/api/merchants/category", UriKind.Relative), new { merchantKey = "krakow zakopian", categoryId = Groceries }, CancellationToken);
        var after = await GetAsync(client, "/api/merchants/to-review");

        Assert.Equal(HttpStatusCode.OK, label.StatusCode);
        Assert.Equal(group.GetProperty("count").GetInt32(), (await label.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("changed").GetInt32());
        Assert.DoesNotContain(after.GetProperty("groups").EnumerateArray(), g => g.GetProperty("merchantKey").GetString() == "krakow zakopian");
        var share = (JsonElement p) => p.GetProperty("progress")[0].GetProperty("share").GetDouble();
        Assert.True(share(after) > share(before));
        Assert.True(share(after) < 1);
    }

    [Fact]
    public async Task Categories_CanBeAddedRenamedAndHidden()
    {
        var client = await _factory.SignInNewUserAsync(CancellationToken);

        using var created = await client.PostAsJsonAsync(new Uri("/api/categories", UriKind.Relative), new { parentId = Restaurants, name = "  Kawa " }, CancellationToken);
        var coffee = await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        using var renamed = await client.PutAsJsonAsync(new Uri($"/api/categories/{Other}", UriKind.Relative), new { name = "Różne", hidden = true }, CancellationToken);
        using var subOfSub = await client.PostAsJsonAsync(new Uri("/api/categories", UriKind.Relative), new { parentId = coffee.GetProperty("id").GetGuid(), name = "Espresso" }, CancellationToken);
        using var empty = await client.PutAsJsonAsync(new Uri($"/api/categories/{coffee.GetProperty("id").GetGuid()}", UriKind.Relative), new { name = " ", hidden = false }, CancellationToken);
        var all = await GetAsync(client, "/api/categories");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(("Kawa", "spending", true), (coffee.GetProperty("name").GetString(), coffee.GetProperty("kind").GetString(), coffee.GetProperty("own").GetBoolean()));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, subOfSub.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var other = all.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == Other);
        Assert.Equal(("other", "Różne", true), (other.GetProperty("key").GetString(), other.GetProperty("name").GetString(), other.GetProperty("hidden").GetBoolean()));
        Assert.Equal(21, all.GetArrayLength());
    }

    [Fact]
    public async Task Labels_ExportTheUsersOwnLabelsAsTopLevelKeys()
    {
        var client = await ImportedUserAsync();
        using var created = await client.PostAsJsonAsync(new Uri("/api/categories", UriKind.Relative), new { parentId = Restaurants, name = "Kawa" }, CancellationToken);
        var coffee = (await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();
        using var label = await client.PostAsJsonAsync(new Uri("/api/merchants/category", UriKind.Relative), new { merchantKey = "starbucks", categoryId = coffee }, CancellationToken);

        using var response = await client.GetAsync(new Uri("/api/classification/labels", UriKind.Relative), CancellationToken);
        var lines = (await response.Content.ReadAsStringAsync(CancellationToken)).TrimEnd('\n').Split('\n');

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("tx_id;tx_date;amount;category", lines[0]);
        Assert.Equal(7, lines.Length - 1);
        Assert.All(lines.Skip(1), l => Assert.EndsWith(";restaurants", l, StringComparison.Ordinal));
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

        // Classified in the background: wait until nothing is left unclassified by the worker.
        for (var i = 0; i < 100; i++)
        {
            var open = await GetAsync(client, "/api/transactions?filter=unclassified&limit=200");
            if (open.GetProperty("items").GetArrayLength() < 50)
            {
                return client;
            }

            await Task.Delay(100, CancellationToken);
        }

        throw new TimeoutException("The import wasn't classified.");
    }

    private static async Task<Guid> FirstAsync(HttpClient client, string merchantKey)
    {
        var page = await GetAsync(client, $"/api/transactions?q={Uri.EscapeDataString(merchantKey)}&limit=200");
        return page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("merchantKey").GetString() == merchantKey).GetProperty("id").GetGuid();
    }

    private static Task<JsonElement> GetAsync(HttpClient client, string path) =>
        client.GetFromJsonAsync<JsonElement>(new Uri(path, UriKind.Relative), CancellationToken);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
