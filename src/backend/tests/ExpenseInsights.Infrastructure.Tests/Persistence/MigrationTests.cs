using ExpenseInsights.Testing;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Tests.Persistence;

public sealed class MigrationTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task Migrations_AreAllApplied()
    {
        await using var db = CreateDbContext();

        var pending = await db.Database.GetPendingMigrationsAsync(CancellationToken);

        Assert.Empty(pending);
    }

    [Fact]
    public async Task Migrations_InstallPgTrgmExtension()
    {
        await using var db = CreateDbContext();

        var installed = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_extension WHERE extname = 'pg_trgm'")
            .SingleAsync(CancellationToken);

        Assert.Equal(1, installed);
    }
}
