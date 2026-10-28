using ExpenseInsights.Infrastructure.Persistence;
using Xunit;

namespace ExpenseInsights.Testing;

/// <summary>Base class for tests that need a real, migrated Postgres database.</summary>
public abstract class IntegrationTestBase(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    protected PostgresFixture Postgres { get; } = postgres;

    protected AppDbContext CreateDbContext() => Postgres.CreateDbContext();

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
}
