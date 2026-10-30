using ExpenseInsights.Core.Users;

namespace ExpenseInsights.Core.Classification;

/// <summary>A user's rename or hide of a global category. The category's meaning stays the same.</summary>
public sealed class CategorySetting : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid CategoryId { get; set; }

    /// <summary>Replaces the i18n name; <c>null</c> keeps it.</summary>
    public string? Name { get; set; }

    public bool IsHidden { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
