using ExpenseInsights.Core.Import;
using ExpenseInsights.Infrastructure.Import;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api;

internal static class ImportEndpoints
{
    public const long MaxFileSize = 5 * 1024 * 1024;

    public static void MapImportEndpoints(this IEndpointRouteBuilder api)
    {
        var imports = api.MapGroup("/imports");

        imports.MapPost("", UploadAsync)
            // Room for the multipart envelope; the file itself is checked against MaxFileSize.
            .WithMetadata(new RequestSizeLimitAttribute(MaxFileSize + (64 * 1024)));

        imports.MapGet("", ListAsync);
    }

    private static async Task<IResult> UploadAsync(IFormFile? file, StatementImporter importer, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(StatusCodes.Status400BadRequest, "fileMissing", "No file was uploaded.");
        }

        if (file.Length > MaxFileSize)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "fileTooLarge", "The file is larger than 5 MB.");
        }

        try
        {
            // Buffered by ASP.NET Core, so it is seekable.
            await using var stream = file.OpenReadStream();
            var (batch, alreadyImported) = await importer.ImportAsync(stream, file.FileName, ct);
            return Results.Ok(ImportBatchResponse.From(batch, alreadyImported));
        }
        catch (StatementFormatException e)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, e.Code, e.Message);
        }
    }

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var batches = await db.ImportBatches.AsNoTracking().OrderByDescending(b => b.ImportedAt).ToListAsync(ct);
        return Results.Ok(batches.Select(b => ImportBatchResponse.From(b, alreadyImported: false)));
    }

    private static IResult Problem(int status, string code, string detail) => ApiProblem.Of(status, code, detail);
}

internal sealed record ImportBatchResponse(
    Guid Id,
    string BankId,
    string FileName,
    DateTimeOffset ImportedAt,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int RowCount,
    int NewCount,
    int UpdatedCount,
    int DuplicateCount,
    int ErrorCount,
    int ExpiredCount,
    CheckResult TotalsCheck,
    CheckResult BalanceCheck,
    IReadOnlyList<ImportIssue> Warnings,
    IReadOnlyList<ImportIssue> Errors,
    bool AlreadyImported)
{
    public static ImportBatchResponse From(ImportBatch b, bool alreadyImported) => new(
        b.Id, b.BankId, b.FileName, b.ImportedAt, b.PeriodStart, b.PeriodEnd, b.RowCount, b.NewCount, b.UpdatedCount,
        b.DuplicateCount, b.ErrorCount, b.ExpiredCount, b.TotalsCheck, b.BalanceCheck, b.Warnings, b.Errors, alreadyImported);
}
