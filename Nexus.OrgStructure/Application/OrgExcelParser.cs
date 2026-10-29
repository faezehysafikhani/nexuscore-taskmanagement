namespace Nexus.OrgStructure.Application;

/// <summary>One row of the sheet as read, header excluded. Row is the sheet's own row number (what the user sees).</summary>
public sealed record ExcelStructureRow(int Row, string? Code, string? Name, string? ParentCode, string? TypeCode = null, string? ManagerPostName = null);

/// <summary>A node of the parsed tree, children in the order of the sheet.</summary>
public sealed record ParsedStructureNode(string Code, string Name, string? TypeCode, string? ManagerPostName, int Row, IReadOnlyList<ParsedStructureNode> Children);

/// <summary>All-or-nothing: either Roots with no errors, or errors with no tree.</summary>
public sealed record ExcelParseResult(IReadOnlyList<ParsedStructureNode> Roots, IReadOnlyList<StructureError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    /// <summary>Every node, each one after its parent (the order to create them in).</summary>
    public IReadOnlyList<ParsedStructureNode> Flatten()
    {
        var result = new List<ParsedStructureNode>();
        void Walk(ParsedStructureNode node)
        {
            result.Add(node);
            node.Children.ToList().ForEach(Walk);
        }

        Roots.ToList().ForEach(Walk);
        return result;
    }
}

/// <summary>
/// Turns the rows of a structure sheet (code, name, parent code, type, optionally the manager's
/// post) into a valid tree, or into the list of everything wrong with the rows, each with its row
/// number. Reading the file itself is not its job, so it needs no library and no database; the
/// structure rules are the validator's. Completely empty rows are skipped.
/// </summary>
public static class OrgExcelParser
{
    public const int MaxNameLength = 200;

    public static ExcelParseResult Parse(
        IEnumerable<ExcelStructureRow> rows,
        IReadOnlyCollection<string>? knownTypeCodes = null,
        IReadOnlyCollection<StructureTypeRule>? rules = null)
    {
        var errors = new List<StructureError>();
        var inputs = new List<StructureNodeInput>();
        var extra = new Dictionary<int, ExcelStructureRow>();

        foreach (var row in rows.Where(row => !IsBlank(row)))
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                errors.Add(new(StructureErrorKind.MissingName, row.Code?.Trim(), row.Row, $"سطر {row.Row}: نام گره خالی است."));
            }
            else if (row.Name.Trim().Length > MaxNameLength)
            {
                errors.Add(new(StructureErrorKind.NameTooLong, row.Code?.Trim(), row.Row, $"سطر {row.Row}: نام گره بیشتر از {MaxNameLength} نویسه است."));
            }

            inputs.Add(new StructureNodeInput(row.Code ?? string.Empty, row.Name ?? string.Empty, row.ParentCode, row.TypeCode, false, row.Row));
            extra[row.Row] = row;
        }

        errors.AddRange(OrgStructureValidator.Validate(inputs, knownTypeCodes, rules));
        if (errors.Count > 0)
        {
            return new ExcelParseResult([], errors.OrderBy(error => error.Row ?? int.MaxValue).ThenBy(error => error.Kind).ToList());
        }

        return new ExcelParseResult(BuildTree(inputs, extra), []);
    }

    private static IReadOnlyList<ParsedStructureNode> BuildTree(List<StructureNodeInput> inputs, Dictionary<int, ExcelStructureRow> extra)
    {
        var childrenOf = inputs.ToLookup(input => Key(input.ParentCode));
        ParsedStructureNode Make(StructureNodeInput input) => new(
            input.Code.Trim(),
            input.Name.Trim(),
            Clean(input.TypeCode),
            Clean(extra[input.Row!.Value].ManagerPostName),
            input.Row!.Value,
            childrenOf[Key(input.Code)].Select(Make).ToList());

        return childrenOf[string.Empty].Select(Make).ToList();
    }

    private static bool IsBlank(ExcelStructureRow row) =>
        string.IsNullOrWhiteSpace(row.Code) && string.IsNullOrWhiteSpace(row.Name) && string.IsNullOrWhiteSpace(row.ParentCode)
        && string.IsNullOrWhiteSpace(row.TypeCode) && string.IsNullOrWhiteSpace(row.ManagerPostName);

    private static string Key(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
