using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Trigram similarity for merchant matching (classification plan 04).
        modelBuilder.HasPostgresExtension("pg_trgm");
    }
}
