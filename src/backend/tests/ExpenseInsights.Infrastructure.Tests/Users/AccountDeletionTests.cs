using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Tests.Import;
using ExpenseInsights.Infrastructure.Users;
using ExpenseInsights.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpenseInsights.Infrastructure.Tests.Users;

public sealed class AccountDeletionTests(PostgresFixture postgres) : ImportTestBase(postgres)
{
    [Fact]
    public async Task DeletesEveryRowOfTheUser_AndNothingOfOthers()
    {
        var deleted = await NewUserAsync();
        var kept = await NewUserAsync();
        foreach (var user in new[] { deleted, kept })
        {
            await ImportAsync(user, "fake_ing.csv");
            await LinkAsync(user);
        }

        var keptBefore = await CountRowsAsync(kept);

        await using (var db = CreateDbContext(deleted))
        {
            await new AccountDeletion(db, NullLogger<AccountDeletion>.Instance).DeleteCurrentUserAsync(CancellationToken);
        }

        Assert.All(await CountRowsAsync(deleted), table => Assert.Equal(0, table.Value));
        Assert.Equal(keptBefore, await CountRowsAsync(kept));
        Assert.Contains(keptBefore, table => table.Key == "Transactions" && table.Value == 335);
    }

    [Fact]
    public async Task TheOwner_CantBeDeleted()
    {
        await using var db = CreateDbContext(User.OwnerId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AccountDeletion(db, NullLogger<AccountDeletion>.Instance).DeleteCurrentUserAsync(CancellationToken));
        Assert.True(await db.Users.AnyAsync(u => u.Id == User.OwnerId, CancellationToken));
    }

    private async Task LinkAsync(Guid userId)
    {
        await using var db = CreateDbContext();
        var email = $"{userId:N}@example.test";
        db.Invitations.Add(new Invitation { Id = Guid.CreateVersion7(), Email = email, UserId = userId, CreatedAt = DateTimeOffset.UtcNow });
        db.ExternalLogins.Add(new ExternalLogin { Id = Guid.CreateVersion7(), UserId = userId, Provider = ExternalLogin.Google, Subject = email, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>
    /// Rows per table that point at the user, for every table with a foreign key to Users (plus
    /// Users itself), read from the EF model: a new user-related entity is covered automatically.
    /// </summary>
    private async Task<Dictionary<string, int>> CountRowsAsync(Guid userId)
    {
        await using var db = CreateDbContext(userId);
        var tables = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys()
                .Where(fk => fk.PrincipalEntityType.ClrType == typeof(User))
                .Select(fk => (Table: e.GetTableName()!, Column: fk.Properties.Single().GetColumnName())))
            .Append((Table: "Users", Column: "Id"))
            .ToList();
        Assert.Contains(tables, t => t.Table == "Transactions");
        Assert.Contains(tables, t => t.Table == "ExternalLogins");
        Assert.Contains(tables, t => t.Table == "Invitations");

        var counts = new Dictionary<string, int>();
        foreach (var (table, column) in tables)
        {
#pragma warning disable EF1002 // Table and column names come from the EF model, not from input.
            counts[table] = await db.Database
                .SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM \"{table}\" WHERE \"{column}\" = {{0}}", userId)
                .SingleAsync(CancellationToken);
#pragma warning restore EF1002
        }

        return counts;
    }
}
