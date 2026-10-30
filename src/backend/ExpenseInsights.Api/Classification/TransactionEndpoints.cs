using System.Globalization;
using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Infrastructure.Classification;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api.Classification;

/// <summary>The transaction list and corrections (plan 04).</summary>
internal static class TransactionEndpoints
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    public const int SuggestionCount = 3;

    public static void MapTransactionEndpoints(this IEndpointRouteBuilder api)
    {
        var transactions = api.MapGroup("/transactions");

        transactions.MapGet("", ListAsync);
        transactions.MapGet("/{id:guid}", GetAsync);
        transactions.MapPut("/{id:guid}/category", CorrectAsync);
        transactions.MapPost("/{id:guid}/apply-to-all", ApplyToAllAsync);
        transactions.MapPut("/{id:guid}/offsets", SetOffsetsAsync);
    }

    /// <summary>Newest first, filtered, searchable; paged by offset for infinite scroll.</summary>
    /// <param name="filter"><c>all</c> (default), <c>unclassified</c> or <c>review</c>.</param>
    /// <param name="q">Searches the counterparty, title and merchant; a number also matches the amount.</param>
    /// <param name="category">Only this category (and its subcategories).</param>
    /// <param name="offset">How many to skip.</param>
    /// <param name="limit">Page size, at most <see cref="MaxLimit"/>.</param>
    /// <param name="db">The database.</param>
    /// <param name="ct">Cancellation.</param>
    private static async Task<IResult> ListAsync(
        string? filter, string? q, Guid? category, int? offset, int? limit, AppDbContext db, CancellationToken ct)
    {
        var query = db.Transactions.AsNoTracking();
        switch (filter)
        {
            case null or "" or "all":
                break;
            case "unclassified":
                query = query.Where(t => t.CategoryId == null);
                break;
            case "review":
                query = query.Where(t => (t.ClassificationSource == ClassificationSource.GlobalRule || t.ClassificationSource == ClassificationSource.UserRule)
                    && t.ClassificationConfidence < Confidence.ReviewBelow);
                break;
            default:
                return ApiProblem.Of(StatusCodes.Status400BadRequest, "invalidFilter", "The filter is all, unclassified or review.");
        }

        if (category is { } categoryId)
        {
            var children = db.Categories.Where(c => c.ParentId == categoryId).Select(c => (Guid?)c.Id);
            query = query.Where(t => t.CategoryId == categoryId || children.Contains(t.CategoryId));
        }

        if (q?.Trim() is { Length: > 0 } search)
        {
            var pattern = $"%{EscapeLike(search)}%";
            var folded = $"%{EscapeLike(ExpenseInsights.Classification.MerchantNormalizer.Fold(search))}%";
            var amount = ParseAmount(search);
            query = query.Where(t => EF.Functions.ILike(t.CounterpartyName, pattern)
                || EF.Functions.ILike(t.TitleRaw, pattern)
                || EF.Functions.ILike(t.MerchantKey, folded)
                || (amount != null && (t.Amount == amount || t.Amount == -amount)));
        }

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var skip = Math.Max(offset ?? 0, 0);
        var page = await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.Id)
            .Skip(skip)
            .Take(take + 1)
            .Select(TransactionItem.Projection)
            .ToListAsync(ct);
        var hasMore = page.Count > take;
        return Results.Ok(new TransactionPage([.. page.Take(take)], hasMore ? skip + take : null));
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, TransactionClassifier classifier, Corrections corrections, CancellationToken ct)
    {
        var transaction = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (transaction is null)
        {
            return NotFound();
        }

        return Results.Ok(await DetailAsync(transaction, db, classifier, corrections, ct));
    }

    /// <summary>The user's correction; returns what "apply to all" would do next.</summary>
    private static Task<IResult> CorrectAsync(Guid id, CategoryRequest request, Corrections corrections, CancellationToken ct) =>
        Handle(async () =>
        {
            if (request.CategoryId is not { } categoryId)
            {
                return ApiProblem.Of(StatusCodes.Status400BadRequest, Corrections.CategoryNotFound, "A category is required.");
            }

            var transaction = await corrections.CorrectAsync(id, categoryId, ct);
            return Results.Ok(new CorrectionResponse(TransactionItem.From(transaction), await corrections.OfferAsync(transaction, ct)));
        });

    private static Task<IResult> ApplyToAllAsync(Guid id, Corrections corrections, CancellationToken ct) =>
        Handle(async () =>
        {
            var (rule, changed) = await corrections.ApplyToAllAsync(id, ct);
            return Results.Ok(new ApplyToAllResponse(RuleResponse.From(rule), changed));
        });

    /// <summary>Marks incoming money as paying back a spending category, or clears it (<c>null</c>).</summary>
    private static Task<IResult> SetOffsetsAsync(Guid id, CategoryRequest request, Corrections corrections, CancellationToken ct) =>
        Handle(async () => Results.Ok(TransactionItem.From(await corrections.SetOffsetsAsync(id, request.CategoryId, ct))));

    internal static async Task<TransactionDetail> DetailAsync(
        Transaction t, AppDbContext db, TransactionClassifier classifier, Corrections corrections, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().Where(a => a.Id == t.AccountId).Select(a => a.Name).SingleAsync(ct);
        var suggestions = await SuggestAsync(t, db, classifier, ct);
        return new TransactionDetail(
            TransactionItem.From(t),
            t.TitleRaw,
            t.CounterpartyRaw,
            t.CounterpartyAccount is { } number ? AccountNumber.Mask(number) : null,
            t.CounterpartyBank,
            account,
            t.BookingDate,
            t.PurchaseDate,
            t.OriginalAmount,
            t.OriginalCurrency,
            MaskAccount(t.RawRow.Fields, t.CounterpartyAccount),
            suggestions,
            await corrections.OfferAsync(t, ct));
    }

    /// <summary>
    /// Up to three other categories it most likely is: the rules' next matches, then what the user
    /// picked for the same merchant, then the usual ones for its direction.
    /// </summary>
    private static async Task<IReadOnlyList<Guid>> SuggestAsync(Transaction t, AppDbContext db, TransactionClassifier classifier, CancellationToken ct)
    {
        var fromRules = (await classifier.MatchesAsync(t, ct)).Select(m => m.CategoryId);
        var fromMerchant = await db.Transactions.AsNoTracking()
            .Where(o => o.MerchantKey == t.MerchantKey && o.Id != t.Id && o.CategoryId != null)
            .GroupBy(o => o.CategoryId!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(SuggestionCount)
            .ToListAsync(ct);
        var usual = t.Amount < 0
            ? new[] { CategoryCatalog.Groceries, CategoryCatalog.Restaurants, CategoryCatalog.TransfersToPeople, CategoryCatalog.Other }
            : [CategoryCatalog.TransfersFromPeople, CategoryCatalog.OtherIncome, CategoryCatalog.Refunds, CategoryCatalog.Salary];
        var hidden = await HiddenAsync(db, ct);
        return [.. fromRules.Concat(fromMerchant).Concat(usual.Select(c => c.Id))
            .Where(id => id != t.CategoryId && !hidden.Contains(id))
            .Distinct()
            .Take(SuggestionCount)];
    }

    private static async Task<HashSet<Guid>> HiddenAsync(AppDbContext db, CancellationToken ct)
    {
        var settings = await db.CategorySettings.Where(s => s.IsHidden).Select(s => s.CategoryId).ToListAsync(ct);
        var own = await db.Categories.Where(c => c.UserId != null && c.IsHidden).Select(c => c.Id).ToListAsync(ct);
        return [.. settings, .. own];
    }

    /// <summary>The raw columns with the counterparty's account number masked, as everywhere else.</summary>
    private static List<string> MaskAccount(IReadOnlyList<string> fields, string? account) =>
        AccountNumber.Normalize(account) is { } number
            ? [.. fields.Select(f => AccountNumber.Normalize(f.Trim('\'', ' ')) == number ? AccountNumber.Mask(number) : f)]
            : [.. fields];

    internal static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CorrectionException e)
        {
            var status = e.Code is Corrections.TransactionNotFound or Corrections.RuleNotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            return ApiProblem.Of(status, e.Code, e.Message);
        }
    }

    private static IResult NotFound() =>
        ApiProblem.Of(StatusCodes.Status404NotFound, Corrections.TransactionNotFound, "There is no such transaction.");

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary><c>23,50</c>, <c>23.50</c>, <c>-23,5 zł</c> → 23.50; anything else <c>null</c>.</summary>
    private static decimal? ParseAmount(string text)
    {
        var cleaned = text.Replace("zł", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "", StringComparison.Ordinal).Replace(' '.ToString(), "", StringComparison.Ordinal).Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? Math.Abs(value)
            : null;
    }
}

internal sealed record CategoryRequest(Guid? CategoryId);

internal sealed record TransactionPage(IReadOnlyList<TransactionItem> Items, int? NextOffset);

/// <remarks>
/// <para><c>Merchant</c>: For display: the shop's domain for payments through a processor, else the counterparty.</para>
/// <para><c>NeedsReview</c>: A guess for the "Do sprawdzenia" queue.</para>
/// <para><c>Reason</c>: Why it got its category, as an i18n code; <see cref="ReasonValue"/> is the matched pattern.</para>
/// </remarks>
internal sealed record TransactionItem(
    Guid Id,
    DateOnly Date,
    TransactionStatus Status,
    decimal Amount,
    string Currency,
    TransactionKind Kind,
    string Merchant,
    string MerchantKey,
    Guid? CategoryId,
    ClassificationSource Source,
    double Confidence,
    bool NeedsReview,
    string? Reason,
    string? ReasonValue,
    Guid? OffsetsCategoryId)
{
    public static readonly System.Linq.Expressions.Expression<Func<Transaction, TransactionItem>> Projection = t => new TransactionItem(
        t.Id,
        t.TransactionDate,
        t.Status,
        t.Amount,
        t.Currency,
        t.Kind,
        t.MerchantHint ?? t.CounterpartyName,
        t.MerchantKey,
        t.CategoryId,
        t.ClassificationSource,
        t.ClassificationConfidence,
        (t.ClassificationSource == ClassificationSource.GlobalRule || t.ClassificationSource == ClassificationSource.UserRule)
            && t.ClassificationConfidence < Core.Classification.Confidence.ReviewBelow,
        t.ClassificationReason,
        t.ClassificationReasonValue,
        t.OffsetsCategoryId);

    private static readonly Func<Transaction, TransactionItem> Compiled = Projection.Compile();

    public static TransactionItem From(Transaction t) => Compiled(t);
}

/// <remarks>
/// <para><c>CounterpartyAccount</c>: Masked (<c>•••• 1234</c>).</para>
/// <para><c>Raw</c>: The original CSV columns.</para>
/// <para><c>Suggestions</c>: Up to three other likely categories, best first.</para>
/// <para><c>ApplyToAll</c>: What "apply to all" would do, once it has a category.</para>
/// </remarks>
internal sealed record TransactionDetail(
    TransactionItem Transaction,
    string Title,
    string CounterpartyRaw,
    string? CounterpartyAccount,
    string? CounterpartyBank,
    string AccountName,
    DateOnly? BookingDate,
    DateOnly? PurchaseDate,
    decimal? OriginalAmount,
    string? OriginalCurrency,
    IReadOnlyList<string> Raw,
    IReadOnlyList<Guid> Suggestions,
    ApplyToAllOffer? ApplyToAll);

internal sealed record CorrectionResponse(TransactionItem Transaction, ApplyToAllOffer? ApplyToAll);

internal sealed record ApplyToAllResponse(RuleResponse Rule, int Changed);
