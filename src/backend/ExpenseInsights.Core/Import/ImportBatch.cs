using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Core.Import;

/// <summary>One uploaded statement file and what importing it did.</summary>
public sealed class ImportBatch : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string BankId { get; set; }

    public required string FileName { get; set; }

    /// <summary>SHA-256 of the file; re-uploading the same file is a no-op.</summary>
    public required string FileHash { get; set; }

    public long FileSize { get; set; }

    public string? DocumentId { get; set; }

    public DateTimeOffset? GeneratedAt { get; set; }

    /// <summary>The date range the statement covers, as it declares it.</summary>
    public DateOnly? PeriodStart { get; set; }

    public DateOnly? PeriodEnd { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    public int RowCount { get; set; }

    public int NewCount { get; set; }

    public int UpdatedCount { get; set; }

    public int DuplicateCount { get; set; }

    public int ErrorCount { get; set; }

    /// <summary>Earlier pending rows this import marked as expired.</summary>
    public int ExpiredCount { get; set; }

    public CheckResult TotalsCheck { get; set; }

    public CheckResult BalanceCheck { get; set; }

    public List<ImportIssue> Warnings { get; set; } = [];

    public List<ImportIssue> Errors { get; set; } = [];
}

public enum CheckResult
{
    NotAvailable,
    Passed,
    Failed,
}
