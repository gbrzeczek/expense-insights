using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExpenseInsights.Api.Tests;

public sealed class UnhealthyDatabaseTests
{
    [Fact]
    public async Task Health_ReturnsServiceUnavailable_WhenDatabaseIsUnreachable()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1");
            builder.UseSetting("Database:MigrateOnStartup", "false");
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
