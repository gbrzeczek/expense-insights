using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Core.Users;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    /// <summary>ASP.NET Core Data Protection keys (auth cookie, antiforgery), so sessions survive restarts.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Every <see cref="IUserOwned"/> query is filtered to this user (hard rule 7).</summary>
    public Guid CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Trigram similarity for merchant matching (classification plan 04).
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<Account>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<Transaction>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<ImportBatch>().HasQueryFilter(e => e.UserId == CurrentUserId);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>New user-owned rows belong to the current user; nobody writes another user's rows.</summary>
    private void StampOwnership()
    {
        foreach (var entry in ChangeTracker.Entries<IUserOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.UserId == Guid.Empty)
            {
                entry.Entity.UserId = CurrentUserId;
            }

            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && entry.Entity.UserId != CurrentUserId)
            {
                throw new InvalidOperationException($"A {entry.Metadata.ClrType.Name} of another user can't be saved.");
            }
        }
    }
}
