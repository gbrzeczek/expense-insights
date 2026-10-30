using System.Globalization;
using System.Text;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Infrastructure.Classification;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api.Classification;

/// <summary>
/// First-import onboarding (review by merchant, not by transaction), the user's rules, and running
/// or exporting the classification.
/// </summary>
internal static class MerchantEndpoints
{
    public const int MaxGroups = 100;

    public static void MapMerchantEndpoints(this IEndpointRouteBuilder api)
    {
        var merchants = api.MapGroup("/merchants");
        merchants.MapGet("/to-review", ToReviewAsync);
        merchants.MapPost("/category", LabelAsync);

        var rules = api.MapGroup("/rules");
        rules.MapGet("", ListRulesAsync);
        rules.MapDelete("/{id:guid}", DeleteRuleAsync);

        var classification = api.MapGroup("/classification");
        classification.MapPost("/run", Run);
        classification.MapGet("/labels", ExportLabelsAsync);
    }

    /// <summary>
    /// Merchants with transactions that need a decision (unclassified or a guess), biggest first
    /// (count × total amount), plus how much of the spending is already categorized.
    /// </summary>
    private static async Task<IResult> ToReviewAsync(AppDbContext db, CancellationToken ct)
    {
        var open = db.Transactions.AsNoTracking().Where(t => t.CategoryId == null
            || ((t.ClassificationSource == ClassificationSource.GlobalRule || t.ClassificationSource == ClassificationSource.UserRule)
                && t.ClassificationConfidence < Confidence.ReviewBelow));
        // A few thousand rows at most: grouped in memory, which keeps the SQL simple.
        var rows = await open
            .Select(t => new { t.MerchantKey, t.Currency, t.Amount, Name = t.MerchantHint ?? t.CounterpartyName, t.CategoryId })
            .ToListAsync(ct);
        var groups = rows
            .GroupBy(t => (t.MerchantKey, t.Currency))
            .Select(g => new
            {
                g.Key.MerchantKey,
                g.Key.Currency,
                Count = g.Count(),
                Total = g.Sum(t => t.Amount),
                Name = g.GroupBy(t => t.Name).OrderByDescending(n => n.Count()).First().Key,
                Guess = g.Select(t => t.CategoryId).FirstOrDefault(c => c != null),
            })
            .ToList();

        // Spending excludes own transfers and money coming in.
        var spending = (await db.Transactions.AsNoTracking()
                .Where(t => t.Amount < 0 && t.Kind != TransactionKind.OwnTransfer && t.CategoryId != CategoryCatalog.OwnTransfer.Id)
                .Select(t => new
                {
                    t.Currency,
                    t.Amount,
                    Categorized = t.CategoryId != null
                        && !((t.ClassificationSource == ClassificationSource.GlobalRule || t.ClassificationSource == ClassificationSource.UserRule)
                            && t.ClassificationConfidence < Confidence.ReviewBelow),
                })
                .ToListAsync(ct))
            .GroupBy(t => t.Currency)
            .Select(g => new { Currency = g.Key, Total = -g.Sum(t => t.Amount), Categorized = -g.Where(t => t.Categorized).Sum(t => t.Amount) })
            .ToList();

        return Results.Ok(new MerchantReview(
            [.. groups
                .OrderByDescending(g => g.Count * Math.Abs(g.Total))
                .Take(MaxGroups)
                .Select(g => new MerchantGroup(g.MerchantKey, g.Name, g.Count, g.Total, g.Currency, g.Guess))],
            [.. spending
                .OrderByDescending(s => s.Total)
                .Select(s => new SpendingProgress(s.Currency, s.Categorized, s.Total, s.Total == 0 ? 1 : (double)(s.Categorized / s.Total)))]));
    }

    /// <summary>Labels every transaction from a merchant with one tap; creates a rule for future imports.</summary>
    private static Task<IResult> LabelAsync(LabelMerchantRequest request, Corrections corrections, CancellationToken ct) =>
        TransactionEndpoints.Handle(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.MerchantKey) || request.CategoryId is not { } categoryId)
            {
                return ApiProblem.Of(StatusCodes.Status400BadRequest, "invalidRequest", "A merchant and a category are required.");
            }

            var (rule, changed) = await corrections.LabelMerchantAsync(request.MerchantKey, categoryId, ct);
            return Results.Ok(new ApplyToAllResponse(RuleResponse.From(rule), changed));
        });

    private static async Task<IResult> ListRulesAsync(AppDbContext db, CancellationToken ct)
    {
        var rules = await db.MerchantRules.AsNoTracking().OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return Results.Ok(rules.Select(RuleResponse.From));
    }

    private static Task<IResult> DeleteRuleAsync(Guid id, Corrections corrections, CancellationToken ct) =>
        TransactionEndpoints.Handle(async () => Results.Ok(new DeleteRuleResponse(await corrections.DeleteRuleAsync(id, ct))));

    /// <summary>Reclassifies all of the user's transactions in the background.</summary>
    private static IResult Run(AppDbContext db, ClassificationRequests requests)
    {
        requests.Enqueue(db.CurrentUserId);
        return Results.Accepted();
    }

    /// <summary>
    /// The user's own labels (corrections and rules they made) as a CSV for the Eval tool: it
    /// measures real accuracy on a local export without committing real data. Matched by bank id.
    /// </summary>
    private static async Task<IResult> ExportLabelsAsync(AppDbContext db, CancellationToken ct)
    {
        var parents = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.ExternalId != null && t.CategoryId != null
                && (t.ClassificationSource == ClassificationSource.UserCorrection || t.ClassificationSource == ClassificationSource.UserRule))
            .OrderBy(t => t.TransactionDate)
            .Select(t => new { t.ExternalId, t.TransactionDate, t.Amount, t.CategoryId })
            .ToListAsync(ct);

        var csv = new StringBuilder("tx_id;tx_date;amount;category\n");
        foreach (var row in rows)
        {
            // Labels are top-level keys: the taxonomy everyone shares.
            var category = parents[row.CategoryId!.Value];
            while (category.ParentId is { } parent)
            {
                category = parents[parent];
            }

            csv.Append(CultureInfo.InvariantCulture, $"{row.ExternalId};{row.TransactionDate:yyyy-MM-dd};{row.Amount.ToString("0.00", CultureInfo.InvariantCulture)};{category.Key}\n");
        }

        return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "labels.csv");
    }
}

internal sealed record LabelMerchantRequest(string? MerchantKey, Guid? CategoryId);

/// <remarks>
/// <para><c>Guess</c>: The category a guess in the group has, if any: a likely first chip.</para>
/// </remarks>
internal sealed record MerchantGroup(string MerchantKey, string Name, int Count, decimal Total, string Currency, Guid? Guess);

/// <remarks>
/// <para><c>Share</c>: Categorized ÷ total, by amount (0–1).</para>
/// </remarks>
internal sealed record SpendingProgress(string Currency, decimal Categorized, decimal Total, double Share);

internal sealed record MerchantReview(IReadOnlyList<MerchantGroup> Groups, IReadOnlyList<SpendingProgress> Progress);

internal sealed record DeleteRuleResponse(int Changed);

/// <remarks>
/// <para><c>Label</c>: The merchant or the person's name. An account rule's pattern (a hash) is never returned.</para>
/// </remarks>
internal sealed record RuleResponse(Guid Id, RuleMatchType MatchType, string? Pattern, string? Label, Guid CategoryId, DateTimeOffset CreatedAt)
{
    public static RuleResponse From(MerchantRule r) =>
        new(r.Id, r.MatchType, r.MatchType == RuleMatchType.CounterpartyAccount ? null : r.Pattern, r.Label, r.CategoryId, r.CreatedAt);
}
