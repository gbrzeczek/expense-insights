using ExpenseInsights.Core.Users;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseInsights.Infrastructure.Users;

/// <summary>
/// Deletes a user and everything they own: these are other people's bank records, so nothing is
/// kept (GDPR-style erasure). Logins and the invitation go with the user (cascade), so signing in
/// again needs a new invitation.
/// </summary>
public sealed partial class AccountDeletion(AppDbContext db, ILogger<AccountDeletion> logger)
{
    /// <summary>Deletes the current user. The owner can't be deleted: they manage the invitations.</summary>
    public async Task DeleteCurrentUserAsync(CancellationToken ct)
    {
        var userId = db.CurrentUserId;
        if (userId == Guid.Empty || userId == User.OwnerId)
        {
            throw new InvalidOperationException("Only a signed-in user other than the owner can be deleted.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Children first: a transaction restricts deleting its import batch and its categories.
        await db.ClassificationHistory.Where(h => h.UserId == userId).ExecuteDeleteAsync(ct);
        await db.TransactionSplits.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Transactions.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
        await db.MerchantRules.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        await db.MerchantEmbeddings.Where(e => e.UserId == userId).ExecuteDeleteAsync(ct);
        await db.CategorySettings.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        // Subcategories before their parents.
        await db.Categories.Where(c => c.UserId == userId && c.ParentId != null).ExecuteDeleteAsync(ct);
        await db.Categories.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ImportBatches.Where(b => b.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Accounts.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);

        LogDeleted(logger, userId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted user {UserId} and all their data")]
    private static partial void LogDeleted(ILogger logger, Guid userId);
}
