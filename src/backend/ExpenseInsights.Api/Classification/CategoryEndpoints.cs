using ExpenseInsights.Core.Classification;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseInsights.Api.Classification;

/// <summary>
/// The categories as the user sees them: the global taxonomy (renamed or hidden per user) plus
/// their own subcategories. The top-level meaning (spending, income, …) never changes.
/// </summary>
internal static class CategoryEndpoints
{
    public const int MaxNameLength = 60;

    public static void MapCategoryEndpoints(this IEndpointRouteBuilder api)
    {
        var categories = api.MapGroup("/categories");

        categories.MapGet("", ListAsync);
        categories.MapPost("", CreateAsync);
        categories.MapPut("/{id:guid}", UpdateAsync);
    }

    public static async Task<List<CategoryResponse>> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var settings = await db.CategorySettings.AsNoTracking().ToDictionaryAsync(s => s.CategoryId, ct);
        return [.. categories
            .Select(c => CategoryResponse.From(c, settings.GetValueOrDefault(c.Id)))
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name, StringComparer.CurrentCulture)];
    }

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct) => Results.Ok(await LoadAsync(db, ct));

    /// <summary>A subcategory of a top-level category; it inherits the parent's kind.</summary>
    private static async Task<IResult> CreateAsync(CreateCategoryRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (CleanName(request.Name) is not { } name)
        {
            return InvalidName();
        }

        var parent = await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.ParentId, ct);
        if (parent is null || parent.ParentId is not null)
        {
            return ApiProblem.Of(StatusCodes.Status400BadRequest, "invalidParent", "A subcategory needs a top-level parent.");
        }

        var category = new Category
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            ParentId = parent.Id,
            Kind = parent.Kind,
            SortOrder = parent.SortOrder,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/categories/{category.Id}", CategoryResponse.From(category, null));
    }

    /// <summary>
    /// Renames or hides a category. A global one keeps its meaning: the change is the user's
    /// setting, and a <c>null</c> name brings back the default.
    /// </summary>
    private static async Task<IResult> UpdateAsync(Guid id, UpdateCategoryRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return ApiProblem.Of(StatusCodes.Status404NotFound, "categoryNotFound", "There is no such category.");
        }

        var name = CleanName(request.Name);
        if (request.Name is not null && name is null)
        {
            return InvalidName();
        }

        if (category.UserId is null)
        {
            var setting = await db.CategorySettings.SingleOrDefaultAsync(s => s.CategoryId == id, ct);
            if (setting is null)
            {
                setting = new CategorySetting { Id = Guid.CreateVersion7(), CategoryId = id };
                db.CategorySettings.Add(setting);
            }

            setting.Name = name;
            setting.IsHidden = request.Hidden;
            setting.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(CategoryResponse.From(category, setting));
        }

        if (name is null)
        {
            return InvalidName();
        }

        category.Name = name;
        category.IsHidden = request.Hidden;
        await db.SaveChangesAsync(ct);
        return Results.Ok(CategoryResponse.From(category, null));
    }

    private static string? CleanName(string? name) =>
        name?.Trim() is { Length: > 0 and <= MaxNameLength } trimmed ? trimmed : null;

    private static IResult InvalidName() =>
        ApiProblem.Of(StatusCodes.Status400BadRequest, "invalidName", $"A name needs 1–{MaxNameLength} characters.");
}

internal sealed record CreateCategoryRequest(Guid ParentId, string? Name);

internal sealed record UpdateCategoryRequest(string? Name, bool Hidden);

/// <remarks>
/// <para><c>Key</c>: The i18n key of a global category; the UI names it unless <paramref name="Name"/> is set.</para>
/// <para><c>Name</c>: The user's name for it, if they gave one.</para>
/// <para><c>Own</c>: The user's own subcategory.</para>
/// </remarks>
internal sealed record CategoryResponse(
    Guid Id, string? Key, string? Name, Guid? ParentId, CategoryKind Kind, int SortOrder, bool Hidden, bool Own)
{
    public static CategoryResponse From(Category c, CategorySetting? setting) => c.UserId is null
        ? new(c.Id, c.Key, setting?.Name, c.ParentId, c.Kind, c.SortOrder, setting?.IsHidden ?? false, Own: false)
        : new(c.Id, null, c.Name, c.ParentId, c.Kind, c.SortOrder, c.IsHidden, Own: true);
}
