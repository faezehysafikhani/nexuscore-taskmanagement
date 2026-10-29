namespace Nexus.OrgStructure.Application;

/// <summary>One node as the validator sees it. Row is where it came from (an Excel row, a form line), only for the message.</summary>
public sealed record StructureNodeInput(string Code, string Name, string? ParentCode, string? TypeCode = null, bool IsLinkedToCompany = false, int? Row = null);

/// <summary>What may go under what: a node of the child type may sit under a node of the parent type (null parent: at the top).</summary>
public sealed record StructureTypeRule(string? ParentTypeCode, string ChildTypeCode);

public enum StructureErrorKind
{
    MissingCode,
    DuplicateCode,
    MissingParent,
    Cycle,
    UnknownType,
    TypeNotAllowedHere,
    LinkNotOnTopNode,
}

/// <summary>One thing wrong with a structure. Message is for people; Kind is for code and tests.</summary>
public sealed record StructureError(StructureErrorKind Kind, string? NodeCode, int? Row, string Message);

/// <summary>
/// The rules of a sound structure, in one place and without a database: the node API, the Excel
/// import and the templates all ask it. It lists every fault it finds - it never stops at the first.
/// Codes are compared without case and surrounding spaces. A structure with no type rules at all
/// is not restricted; a node with no type is not checked against the rules.
/// </summary>
public static class OrgStructureValidator
{
    public static IReadOnlyList<StructureError> Validate(
        IReadOnlyList<StructureNodeInput> nodes,
        IReadOnlyCollection<string>? knownTypeCodes = null,
        IReadOnlyCollection<StructureTypeRule>? rules = null)
    {
        var errors = new List<StructureError>();
        var byCode = new Dictionary<string, StructureNodeInput>();
        var cleanNodes = new List<StructureNodeInput>();

        foreach (var node in nodes)
        {
            var code = Normalize(node.Code);
            if (code.Length == 0)
            {
                errors.Add(new(StructureErrorKind.MissingCode, null, node.Row, $"{At(node.Row)}کد گره خالی است."));
                continue;
            }

            if (!byCode.TryAdd(code, node))
            {
                errors.Add(new(StructureErrorKind.DuplicateCode, node.Code.Trim(), node.Row,
                    $"{At(node.Row)}کد «{node.Code.Trim()}» تکراری است{FirstAt(byCode[code].Row)}."));
                continue;
            }

            cleanNodes.Add(node);
        }

        var cyclic = new HashSet<string>();
        foreach (var node in cleanNodes)
        {
            var parent = Normalize(node.ParentCode);
            if (parent.Length > 0 && !byCode.ContainsKey(parent))
            {
                errors.Add(new(StructureErrorKind.MissingParent, node.Code.Trim(), node.Row,
                    $"{At(node.Row)}والد «{node.ParentCode!.Trim()}» برای گره «{node.Code.Trim()}» وجود ندارد."));
            }
        }

        FindCycles(cleanNodes, byCode, cyclic, errors);

        var types = knownTypeCodes is null ? null : new HashSet<string>(knownTypeCodes.Select(Normalize));
        var allowed = rules is { Count: > 0 }
            ? new HashSet<(string, string)>(rules.Select(rule => (Normalize(rule.ParentTypeCode), Normalize(rule.ChildTypeCode))))
            : null;

        foreach (var node in cleanNodes)
        {
            var code = node.Code.Trim();
            var type = Normalize(node.TypeCode);
            var parent = Normalize(node.ParentCode);
            var hasParent = parent.Length > 0 && byCode.ContainsKey(parent);

            var typeKnown = type.Length == 0 || types is null || types.Contains(type);
            if (!typeKnown)
            {
                errors.Add(new(StructureErrorKind.UnknownType, code, node.Row, $"{At(node.Row)}نوع «{node.TypeCode!.Trim()}» برای گره «{code}» تعریف نشده است."));
            }

            // Only a node whose place is clear is checked against the rules (a missing or looping parent has its own error).
            var placed = parent.Length == 0 || (hasParent && !cyclic.Contains(code));
            if (allowed is not null && type.Length > 0 && typeKnown && placed)
            {
                var parentType = parent.Length == 0 ? string.Empty : Normalize(byCode[parent].TypeCode);
                var parentTyped = parent.Length == 0 || parentType.Length > 0;
                if (parentTyped && !allowed.Contains((parentType, type)))
                {
                    var where = parent.Length == 0 ? "در بالاترین سطح" : $"زیر «{byCode[parent].Code.Trim()}» ({byCode[parent].TypeCode!.Trim()})";
                    errors.Add(new(StructureErrorKind.TypeNotAllowedHere, code, node.Row,
                        $"{At(node.Row)}گره «{code}» از نوع «{node.TypeCode!.Trim()}» را نمی‌شود {where} گذاشت."));
                }
            }

            if (node.IsLinkedToCompany && parent.Length > 0)
            {
                errors.Add(new(StructureErrorKind.LinkNotOnTopNode, code, node.Row,
                    $"{At(node.Row)}لینک شرکت فقط روی گره‌ی بالایی ممکن است؛ «{code}» زیر «{node.ParentCode!.Trim()}» است."));
            }
        }

        return errors.OrderBy(error => error.Row ?? int.MaxValue).ThenBy(error => error.Kind).ToList();
    }

    /// <summary>Each loop is reported once, on the first of its nodes in the given order, with the whole path.</summary>
    private static void FindCycles(List<StructureNodeInput> nodes, Dictionary<string, StructureNodeInput> byCode, HashSet<string> cyclic, List<StructureError> errors)
    {
        var done = new HashSet<string>();
        foreach (var start in nodes)
        {
            var path = new List<string>();
            var onPath = new Dictionary<string, int>();
            var current = Normalize(start.Code);
            while (current.Length > 0 && byCode.ContainsKey(current) && !done.Contains(current))
            {
                if (onPath.TryGetValue(current, out var at))
                {
                    var loop = path.Skip(at).ToList();
                    loop.ForEach(code => cyclic.Add(code));
                    var members = nodes.Where(node => loop.Contains(Normalize(node.Code))).ToList();
                    var first = members[0];
                    errors.Add(new(StructureErrorKind.Cycle, first.Code.Trim(), first.Row,
                        $"{At(first.Row)}حلقه در والدها: {string.Join(" ← ", members.Select(node => node.Code.Trim()).Append(first.Code.Trim()))}."));
                    break;
                }

                onPath[current] = path.Count;
                path.Add(current);
                current = Normalize(byCode[current].ParentCode);
            }

            path.ForEach(code => done.Add(code));
        }
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string At(int? row) => row is null ? string.Empty : $"سطر {row}: ";

    private static string FirstAt(int? row) => row is null ? string.Empty : $" (اولین بار در سطر {row})";
}
