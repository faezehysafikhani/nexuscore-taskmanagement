using System.Text;
using System.Text.Json;
using Nexus.OrgStructure.Application;

namespace Nexus.CompositionTests;

/// <summary>M4: the rows of a structure sheet become a valid tree or a list of errors with row numbers.</summary>
public sealed class OrgExcelParserTests
{
    private static ExcelStructureRow R(int row, string? code, string? name, string? parent, string? type = null, string? manager = null) =>
        new(row, code, name, parent, type, manager);

    private static List<ExcelStructureRow> ReadSample(string file)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Samples", file), Encoding.UTF8);
        return lines.Skip(1).Select((line, index) =>
        {
            var cells = line.Split(',');
            string? At(int i) => i < cells.Length ? cells[i] : null;
            return new ExcelStructureRow(index + 2, At(0), At(1), At(2), At(3), At(4));
        }).ToList();
    }

    private static (List<string> Types, List<StructureTypeRule> Rules) BankTypesAndRules()
    {
        using var stream = typeof(OrgStructureValidator).Assembly.GetManifestResourceStream("Nexus.OrgStructure.Templates.bank.json")!;
        using var json = JsonDocument.Parse(stream);
        var types = json.RootElement.GetProperty("nodeTypes").EnumerateArray().Select(type => type.GetProperty("key").GetString()!).ToList();
        var rules = json.RootElement.GetProperty("rules").EnumerateArray()
            .Select(rule => new StructureTypeRule(rule.GetProperty("parent").GetString(), rule.GetProperty("child").GetString()!)).ToList();
        return (types, rules);
    }

    [Fact]
    public void TheBankSample_IsAValidTree_WithFortyNodes_ParentsFirst()
    {
        var (types, rules) = BankTypesAndRules();
        var result = OrgExcelParser.Parse(ReadSample("bank-structure.csv"), types, rules);

        Assert.True(result.IsValid, string.Join("\n", result.Errors.Select(error => error.Message)));
        var flat = result.Flatten();
        Assert.Equal(40, flat.Count);
        var root = Assert.Single(result.Roots);
        Assert.Equal("مجمع عمومی", root.Name);
        Assert.Equal("مدیرعامل", flat.Single(node => node.Code == "B03").ManagerPostName);
        Assert.Null(flat.Single(node => node.Code == "B02").ManagerPostName);

        var seen = new HashSet<string>();
        foreach (var node in flat)
        {
            var parent = ReadSample("bank-structure.csv").Single(row => row.Code == node.Code).ParentCode;
            Assert.True(string.IsNullOrEmpty(parent) || seen.Contains(parent), $"{node.Code} came before its parent");
            seen.Add(node.Code);
        }
    }

    [Fact]
    public void TheFaultySample_ReportsEveryFault_OnItsOwnRow()
    {
        var (types, rules) = BankTypesAndRules();
        var result = OrgExcelParser.Parse(ReadSample("bank-structure-faulty.csv"), types, rules);

        Assert.False(result.IsValid);
        Assert.Empty(result.Roots);
        Assert.Equal(
            [
                (5, StructureErrorKind.MissingName),
                (6, StructureErrorKind.DuplicateCode),
                (7, StructureErrorKind.MissingParent),
                (8, StructureErrorKind.UnknownType),
                (9, StructureErrorKind.TypeNotAllowedHere),
                (10, StructureErrorKind.Cycle),
                (11, StructureErrorKind.Cycle),
            ],
            result.Errors.Select(error => (error.Row!.Value, error.Kind)).ToList());
    }

    [Fact]
    public void OneBadRow_FailsTheWholeSheet()
    {
        var result = OrgExcelParser.Parse([R(2, "A", "الف", null), R(3, "B", "ب", "A"), R(4, "C", "ج", "NOPE")]);
        Assert.Empty(result.Roots);
        Assert.Equal(4, Assert.Single(result.Errors).Row);
    }

    [Fact]
    public void BlankRows_AreSkipped_AndRowNumbersStayTheSheets()
    {
        var result = OrgExcelParser.Parse([R(2, "A", "الف", null), R(3, " ", null, null), R(4, "B", "ب", "A")]);
        Assert.True(result.IsValid);
        Assert.Equal([2, 4], result.Flatten().Select(node => node.Row).ToList());
    }

    [Fact]
    public void ChildrenKeepTheSheetsOrder_AndAChildMayComeBeforeItsParentInTheSheet()
    {
        var result = OrgExcelParser.Parse([R(2, "C2", "دوم", "P"), R(3, "P", "والد", null), R(4, "C1", "اول", "p")]);
        Assert.True(result.IsValid);
        var parent = Assert.Single(result.Roots);
        Assert.Equal(["C2", "C1"], parent.Children.Select(node => node.Code).ToList());
    }

    [Fact]
    public void ANameThatIsTooLong_IsReported()
    {
        var error = Assert.Single(OrgExcelParser.Parse([R(2, "A", new string('ن', OrgExcelParser.MaxNameLength + 1), null)]).Errors);
        Assert.Equal(StructureErrorKind.NameTooLong, error.Kind);
        Assert.Equal(2, error.Row);
    }

    [Fact]
    public void ARowWithAParentButNoCode_IsReportedOnce()
    {
        var error = Assert.Single(OrgExcelParser.Parse([R(2, "A", "الف", null), R(3, null, "بی‌کد", "A")]).Errors);
        Assert.Equal(StructureErrorKind.MissingCode, error.Kind);
        Assert.Equal(3, error.Row);
    }

    [Fact]
    public void AnEmptySheet_IsAValidEmptyTree()
    {
        var result = OrgExcelParser.Parse([]);
        Assert.True(result.IsValid);
        Assert.Empty(result.Roots);
    }
}
