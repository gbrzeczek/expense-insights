using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExpenseInsights.Import.Ing;
using ExpenseInsights.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseInsights.Api.Tests;

public sealed class ImportEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IAsyncDisposable
{
    private static readonly Encoding Windows1250 = CodePagesEncodingProvider.Instance.GetEncoding(1250)!;

    private readonly ApiFactory _factory = new(postgres);

    [Fact]
    public async Task Upload_ImportsTheFile_AndTheSameFileAgainIsANoOp()
    {
        using var client = await _factory.SignInNewUserAsync(CancellationToken);
        var file = TestData.Read("fake_ing.csv");

        using var first = await UploadAsync(client, file);
        using var second = await UploadAsync(client, file);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var batch = await ReadAsync(first);
        Assert.Equal(367, batch.GetProperty("newCount").GetInt32());
        Assert.Equal(0, batch.GetProperty("errorCount").GetInt32());
        Assert.Equal("passed", batch.GetProperty("totalsCheck").GetString());
        Assert.Equal("passed", batch.GetProperty("balanceCheck").GetString());
        Assert.Equal("2026-05-01", batch.GetProperty("periodStart").GetString());
        Assert.False(batch.GetProperty("alreadyImported").GetBoolean());

        var again = await ReadAsync(second);
        Assert.True(again.GetProperty("alreadyImported").GetBoolean());
        Assert.Equal(batch.GetProperty("id").GetString(), again.GetProperty("id").GetString());

        var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/imports", UriKind.Relative), CancellationToken);
        Assert.Contains(list.EnumerateArray(), b => b.GetProperty("id").GetString() == batch.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Upload_ReportsRowErrorsAndWarningsAsCodes()
    {
        using var client = await _factory.SignInNewUserAsync(CancellationToken);
        var bad = FakeIngExport.CardRow("2026-10-26", "-2,00", "'api-2'");
        var text = FakeIngExport.File("2026-10-01", "2026-10-28", FakeIngExport.CardRow("2026-10-27", "-1,00", "'api-1'"), bad)
            .Replace(bad, bad.Replace("-2,00", "-2,0x", StringComparison.Ordinal), StringComparison.Ordinal);

        using var response = await UploadAsync(client, Windows1250.GetBytes(text));

        var batch = await ReadAsync(response);
        var error = Assert.Single(batch.GetProperty("errors").EnumerateArray());
        Assert.Equal("invalidAmount", error.GetProperty("code").GetString());
        Assert.Equal(22, error.GetProperty("lineNumber").GetInt32());
        Assert.Contains(batch.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "totalsCountMismatch");
    }

    [Fact]
    public async Task Upload_RejectsAnUnknownFormat()
    {
        using var client = await _factory.SignInNewUserAsync(CancellationToken);

        using var response = await UploadAsync(client, "Date,Amount\n2026-01-01,-3.50\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unsupportedFormat", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Upload_RejectsFilesOver5MB()
    {
        using var client = await _factory.SignInNewUserAsync(CancellationToken);

        using var response = await UploadAsync(client, new byte[(5 * 1024 * 1024) + 1]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("fileTooLarge", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Upload_WithoutAFile_IsABadRequest()
    {
        using var client = await _factory.SignInNewUserAsync(CancellationToken);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("x"), "other");

        using var response = await client.PostAsync(new Uri("/api/imports", UriKind.Relative), content, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("fileMissing", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Upload_WithoutTheXsrfHeader_IsRejected()
    {
        using var client = await _factory.SignInNewUserAsync(CancellationToken);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using var response = await UploadAsync(client, TestData.Read("fake_ing.csv"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("antiforgery", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public void PaymentProcessors_ComeFromConfiguration()
    {
        var options = _factory.Services.GetRequiredService<IngParserOptions>();

        Assert.Contains("PayU", options.PaymentProcessors);
        Assert.Contains("Cashbill", options.PaymentProcessors);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] file)
    {
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(part, "file", "Lista_transakcji_nr_0123456789_281026.csv");
        return await client.PostAsync(new Uri("/api/imports", UriKind.Relative), content, CancellationToken);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
