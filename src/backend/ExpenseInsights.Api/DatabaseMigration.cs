using ExpenseInsights.Api.Auth;
using ExpenseInsights.Infrastructure.Persistence;
using ExpenseInsights.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api;

internal static partial class DatabaseMigration
{
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>Puts the configured owner's email on the allow-list; without it nobody can sign in.</summary>
    public static async Task SeedOwnerInvitationAsync(this WebApplication app)
    {
        var ownerEmail = app.Services.GetRequiredService<AuthOptions>().OwnerEmail;
        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            LogNoOwnerEmail(app.Logger);
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await OwnerInvitation.EnsureAsync(db, ownerEmail, app.Services.GetRequiredService<TimeProvider>(), CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth:OwnerEmail is not set, so nobody new can sign in")]
    private static partial void LogNoOwnerEmail(ILogger logger);
}
