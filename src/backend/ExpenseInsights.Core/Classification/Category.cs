namespace ExpenseInsights.Core.Classification;

/// <summary>
/// A category: either from the global taxonomy (<see cref="UserId"/> is <c>null</c>, seeded from
/// <see cref="CategoryCatalog"/>, named through i18n by <see cref="Key"/>) or a user's own
/// subcategory of a top-level one (named by <see cref="Name"/>). Queries see the global ones plus
/// the current user's.
/// </summary>
public sealed class Category
{
    public Guid Id { get; set; }

    /// <summary><c>null</c> for the global taxonomy.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The i18n key of a global category (<c>groceries</c>); <c>null</c> for a user's own.</summary>
    public string? Key { get; set; }

    /// <summary>The name of a user's own category; global ones are named through i18n.</summary>
    public string? Name { get; set; }

    public Guid? ParentId { get; set; }

    public CategoryKind Kind { get; set; }

    public int SortOrder { get; set; }

    /// <summary>For a user's own category; global ones are hidden per user with a <see cref="CategorySetting"/>.</summary>
    public bool IsHidden { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
