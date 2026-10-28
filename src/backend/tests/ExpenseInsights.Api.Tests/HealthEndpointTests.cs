using System.Net;
using ExpenseInsights.Testing;

namespace ExpenseInsights.Api.Tests;

public sealed class HealthEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IAsyncDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    [Fact]
    public async Task Health_ReturnsHealthy_WhenDatabaseIsReachable()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsProblemDetails()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
