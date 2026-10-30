using System.Text.Json;
using ExpenseInsights.Core.Classification;

namespace ExpenseInsights.Classification.Eval;

/// <summary>One row of a labels file: the correct category of a statement row.</summary>
/// <param name="Row">1-based data row of the statement, if known.</param>
/// <param name="ExternalId">The bank's transaction id, if the row is booked.</param>
/// <param name="CategoryId">The correct category.</param>
public sealed record Label(int? Row, string? ExternalId, Guid CategoryId);

/// <summary>
/// Reads a labels CSV (<c>;</c>-separated, with a header): <c>test-data/fake_ing.labels.csv</c>, or a
/// local export of the owner's corrections. It needs a <c>category</c> column (a category key, or
/// its Polish name from <c>pl.json</c>) and <c>row</c> and/or <c>tx_id</c> to find the statement row.
/// </summary>
public static class Labels
{
    public static async Task<IReadOnlyList<Label>> ReadAsync(string path, IReadOnlyDictionary<string, Guid> categories, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(path, ct);
        var header = lines[0].Split(';');
        var category = Array.IndexOf(header, "category");
        var row = Array.IndexOf(header, "row");
        var id = Array.IndexOf(header, "tx_id");
        if (category < 0 || (row < 0 && id < 0))
        {
            throw new FormatException($"{path}: needs a 'category' column and 'row' or 'tx_id'.");
        }

        var labels = new List<Label>();
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var fields = line.Split(';');
            var name = fields[category].Trim();
            if (!categories.TryGetValue(name, out var categoryId))
            {
                throw new FormatException($"{path}: unknown category '{name}'.");
            }

            labels.Add(new Label(
                row >= 0 && int.TryParse(fields[row], out var r) ? r : null,
                id >= 0 && fields[id].Trim('\'', ' ') is { Length: > 0 } externalId ? externalId : null,
                categoryId));
        }

        return labels;
    }

    /// <summary>Category keys and their Polish names (the frontend's <c>pl.json</c>) → category id.</summary>
    public static IReadOnlyDictionary<string, Guid> CategoryNames(string? plJsonPath)
    {
        var names = CategoryCatalog.All.ToDictionary(c => c.Key, c => c.Id, StringComparer.OrdinalIgnoreCase);
        if (plJsonPath is not null && File.Exists(plJsonPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(plJsonPath));
            foreach (var entry in document.RootElement.GetProperty("categories").EnumerateObject())
            {
                if (CategoryCatalog.TryGetByKey(entry.Name, out var known))
                {
                    names[entry.Value.GetString()!] = known.Id;
                }
            }
        }

        return names;
    }
}
