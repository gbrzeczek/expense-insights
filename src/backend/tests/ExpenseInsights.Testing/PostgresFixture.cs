using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    /// <param name="userId">The current user; the seeded owner by default.</param>
    /// <param name="loggerFactory">Captures EF Core's logs, if given.</param>
    public AppDbContext CreateDbContext(Guid? userId = null, ILoggerFactory? loggerFactory = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString);
        if (loggerFactory is not null)
        {
            options.UseLoggerFactory(loggerFactory);
        }

        return new AppDbContext(options.Options, new TestCurrentUser(userId ?? User.OwnerId));
    }

    /// <summary>A fresh user, so tests sharing the database don't see each other's data.</summary>
    public async Task<Guid> CreateUserAsync(CancellationToken ct)
    {
        await using var db = CreateDbContext();
        var user = new User { Id = Guid.CreateVersion7(), CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
