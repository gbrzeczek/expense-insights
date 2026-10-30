using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Classification;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api.Classification;

/// <summary>
/// Classifies a user's transactions in the background after an import or on demand. At startup it
/// also picks up transactions that were never classified (imported before plan 04, or interrupted).
/// </summary>
internal sealed partial class ClassificationWorker(
    IServiceScopeFactory scopes,
    ClassificationRequests requests,
    ILogger<ClassificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EnqueueUnclassifiedAsync(stoppingToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // E.g. the database isn't migrated yet; new imports still get classified.
            LogStartupFailed(e);
        }

        await foreach (var userId in requests.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await using var db = UserDb(scope.ServiceProvider, userId);
                var classifier = ActivatorUtilities.CreateInstance<TransactionClassifier>(scope.ServiceProvider, db);
                var changed = await classifier.ClassifyAsync(null, stoppingToken);
                LogClassified(userId, changed);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(e, userId);
            }
        }
    }

    private async Task EnqueueUnclassifiedAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await db.Transactions.IgnoreQueryFilters()
            .Where(t => t.ClassifiedAt == null)
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync(ct);
        users.ForEach(requests.Enqueue);
    }

    /// <summary>A context scoped to <paramref name="userId"/>: the worker has no signed-in user.</summary>
    private static AppDbContext UserDb(IServiceProvider services, Guid userId) =>
        ActivatorUtilities.CreateInstance<AppDbContext>(services, new FixedCurrentUser(userId));

    [LoggerMessage(Level = LogLevel.Information, Message = "Classified transactions of user {UserId}: {Changed} changed")]
    private partial void LogClassified(Guid userId, int changed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Classifying transactions of user {UserId} failed")]
    private partial void LogFailed(Exception e, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Finding unclassified transactions at startup failed")]
    private partial void LogStartupFailed(Exception e);

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;
    }
}
