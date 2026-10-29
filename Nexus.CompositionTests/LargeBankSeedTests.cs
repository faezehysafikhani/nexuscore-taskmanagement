using System.Text.Json;
using Nexus.OrgStructure.Application;

namespace Nexus.CompositionTests;

/// <summary>M5: the large bank seed is a valid structure under the bank template's own rules.</summary>
public sealed class LargeBankSeedTests
{
    private static (List<string> Types, List<StructureTypeRule> Rules) BankTypesAndRules()
    {
        using var stream = typeof(OrgStructureValidator).Assembly.GetManifestResourceStream("Nexus.OrgStructure.Templates.bank.json")!;
        using var json = JsonDocument.Parse(stream);
        return (
            json.RootElement.GetProperty("nodeTypes").EnumerateArray().Select(type => type.GetProperty("key").GetString()!).ToList(),
            json.RootElement.GetProperty("rules").EnumerateArray()
                .Select(rule => new StructureTypeRule(rule.GetProperty("parent").GetString(), rule.GetProperty("child").GetString()!)).ToList());
    }

    [Fact]
    public void TheSeed_IsAValidBankStructure_WithHundredsOfBranches()
    {
        var (types, rules) = BankTypesAndRules();
        var result = OrgExcelParser.Parse(LargeBankSeed.Rows(), types, rules);

        Assert.True(result.IsValid, string.Join("\n", result.Errors.Take(5).Select(error => error.Message)));
        var flat = result.Flatten();
        Assert.True(flat.Count >= 400, $"{flat.Count} nodes");
        Assert.True(flat.Count(node => node.TypeCode == "branch") >= 300);
        Assert.Equal(31, flat.Count(node => node.TypeCode == "branch-management"));
        Assert.Equal(5, flat.Count(node => node.TypeCode == "committee"));
    }

    [Fact]
    public void TheSeedsNames_AreUnique_AndFollowTheConvention()
    {
        var rows = LargeBankSeed.Rows();
        Assert.Equal(rows.Count, rows.Select(row => row.Name).Distinct().Count());
        Assert.All(rows.Where(row => row.TypeCode == "branch"), row => Assert.StartsWith("شعبه", row.Name));
        Assert.All(rows.Where(row => row.TypeCode == "branch-management"), row => Assert.StartsWith("مدیریت شعب استان", row.Name));
        Assert.All(rows.Where(row => row.TypeCode == "general-department"), row => Assert.StartsWith("اداره کل", row.Name));
    }

    [Fact]
    public void TheCsvAndTheJson_HoldTheSameRows()
    {
        var csv = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Samples", "large-bank-structure.csv"));
        var rows = LargeBankSeed.Rows();
        Assert.Equal(rows.Count, csv.Length - 1);
        Assert.StartsWith(rows[^1].Code + ",", csv[^1]);
    }
}
