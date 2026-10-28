using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace ExpenseInsights.Testing;

/// <summary>
/// Starts a throwaway Postgres (same image as deploy/docker-compose.yml) and applies migrations.
/// Share it per test class with <c>IClassFixture&lt;PostgresFixture&gt;</c>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "pgvector/pgvector:pg18";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase("expense_insights")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
