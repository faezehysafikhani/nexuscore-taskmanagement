using System.Text.Json;
using Nexus.OrgStructure.Application;

namespace Nexus.CompositionTests;

/// <summary>
/// M5: a bank-sized structure (400+ nodes: assembly, board, committees, deputies, general departments,
/// a branch management per province and hundreds of branches) for volume tests and demos. The data is
/// Samples/large-bank-structure.json (the same rows as .csv); names follow the bank template's convention.
/// </summary>
public static class LargeBankSeed
{
    private sealed record SeedRow(string Code, string Name, string? ParentCode, string Type, string? ManagerPost);

    public static IReadOnlyList<ExcelStructureRow> Rows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "large-bank-structure.json");
        var rows = JsonSerializer.Deserialize<List<SeedRow>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        // Row 1 of a sheet is its header, as in the Excel import.
        return rows.Select((row, index) => new ExcelStructureRow(index + 2, row.Code, row.Name, row.ParentCode, row.Type, row.ManagerPost)).ToList();
    }
}
