using Nexus.Organization.Domain;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// The nodes of an imported sheet (phase 6.2): checked as a whole (M4 and the rules), made only when
/// nothing is wrong. Only new nodes: a code the company already has is an error.
/// </summary>
public sealed partial class OrgNodeService
{
    private const string ParentSentinel = "~parent~";

    /// <summary>A node of the sheet ready to be made: its parent (another row's code, or null: the import's place), type and manager's position.</summary>
    internal sealed record ImportItem(int Row, string Code, string Name, string? ParentCode, Guid TypeId, string TypeName, Guid? ManagerPositionId, string? ManagerPositionTitle, int Depth);

    internal sealed record ImportPlan(Guid CompanyId, Guid? ParentNodeId, IReadOnlyList<ImportItem> Items, IReadOnlyList<ImportErrorDto> Errors);

    internal async Task<Result<ImportPlan>> PlanImportAsync(Guid tenantId, ImportTargetRequest target, IReadOnlyList<ExcelStructureRow> rows, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        return await PlanImportAsync(tenantId, state, target, rows, cancellationToken);
    }

    private async Task<Result<ImportPlan>> PlanImportAsync(Guid tenantId, NodeState state, ImportTargetRequest target, IReadOnlyList<ExcelStructureRow> rows, CancellationToken cancellationToken)
    {
        if (target.CompanyId is null == target.ParentNodeId is null)
        {
            return Result.Failure<ImportPlan>(Error.Validation("جای گره‌ها را بدهید: یا شرکت (سطح اول) یا یک گره (زیر آن)، نه هر دو."));
        }

        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        Guid companyId;
        OrganizationUnit? parent = null;
        if (target.ParentNodeId is { } parentId)
        {
            if (!state.Units.TryGetValue(parentId, out parent) || !Sees(state, mine, parentId))
            {
                return Result.Failure<ImportPlan>(NodeNotFound());
            }

            if (!parent.IsActive)
            {
                return Result.Failure<ImportPlan>(Error.Validation($"گره‌ی «{parent.Name}» غیرفعال است."));
            }

            if (state.Graph.CompanyOfUnit(parentId) is not { } parentCompany)
            {
                return Result.Failure<ImportPlan>(Error.Validation("گره‌ی مقصد به شرکتی وصل نیست."));
            }

            companyId = parentCompany;
        }
        else
        {
            companyId = target.CompanyId!.Value;
            if (CompanyProblem(state, mine, companyId) is { } companyProblem)
            {
                return Result.Failure<ImportPlan>(companyProblem);
            }
        }

        var types = state.NodeTypes.Values.Where(type => type.IsActive).ToList();
        string CodeOf(Guid? typeId) => typeId is { } t && state.NodeTypes.TryGetValue(t, out var type) ? type.Code : PlainUnitCode;

        // Under a node: its rows with no parent code go under it (a stand-in row of its type).
        static bool Blank(ExcelStructureRow row) => string.IsNullOrWhiteSpace(row.Code) && string.IsNullOrWhiteSpace(row.Name)
            && string.IsNullOrWhiteSpace(row.ParentCode) && string.IsNullOrWhiteSpace(row.TypeCode) && string.IsNullOrWhiteSpace(row.ManagerPostName);
        var sheet = rows.Select(row => Blank(row) ? row : row with
        {
            ParentCode = string.IsNullOrWhiteSpace(row.ParentCode) && parent is not null ? ParentSentinel : row.ParentCode,
            TypeCode = string.IsNullOrWhiteSpace(row.TypeCode) ? PlainUnitCode : row.TypeCode.Trim().ToLowerInvariant(),
        }).ToList();
        if (parent is not null)
        {
            sheet.Insert(0, new ExcelStructureRow(0, ParentSentinel, parent.Name, null, CodeOf(parent.NodeTypeId)));
        }

        var rules = state.Rules.Select(rule => new StructureTypeRule(rule.ParentNodeTypeId is { } p ? CodeOf(p) : null, CodeOf(rule.ChildNodeTypeId))).ToList();
        if (parent is not null && rules.Count > 0)
        {
            // Only for this check: the stand-in row for the node may sit at the top (it is where it is).
            rules.Add(new StructureTypeRule(null, CodeOf(parent.NodeTypeId)));
        }

        var parsed = OrgExcelParser.Parse(sheet, types.Select(type => type.Code).ToList(), rules.Count > 0 ? rules : null);
        var errors = parsed.Errors
            .Where(error => error.NodeCode != ParentSentinel && error.Row != 0)
            .Select(error => new ImportErrorDto(error.Row, error.NodeCode, error.Message))
            .ToList();

        if (!parsed.IsValid && errors.Count == 0)
        {
            errors.Add(new ImportErrorDto(null, null, "فایل با جای انتخاب‌شده جور نیست."));
        }

        // Each row on its own, whatever else is wrong: a code the company has, lengths, the manager's position.
        var positions = (await repository.ListPositionsAsync(tenantId, cancellationToken)).Where(p => p.IsActive).ToList();
        var forCompany = (await repository.ListPositionCompaniesAsync(tenantId, cancellationToken))
            .Where(pc => pc.CompanyId == companyId).Select(pc => pc.PositionId).ToHashSet();
        var usedCodes = state.Units.Values.Where(unit => unit.NodeCode is not null && state.Graph.CompanyOfUnit(unit.Id) == companyId)
            .Select(unit => Normalize(unit.NodeCode)).ToHashSet();
        var managers = new Dictionary<int, Position>();
        foreach (var row in rows.Where(row => !Blank(row) && !string.IsNullOrWhiteSpace(row.Code)))
        {
            var code = row.Code!.Trim();
            if (usedCodes.Contains(Normalize(code)))
            {
                errors.Add(new ImportErrorDto(row.Row, code, $"سطر {FaDigits.Of(row.Row)}: کد «{code}» در این شرکت هست؛ ورود فقط گره‌ی تازه می‌سازد."));
            }

            if (code.Length > MaxNodeCodeLength)
            {
                errors.Add(new ImportErrorDto(row.Row, code, $"سطر {FaDigits.Of(row.Row)}: کد گره حداکثر {FaDigits.Of(MaxNodeCodeLength)} نویسه است."));
            }

            if (row.Name?.Trim().Length > MaxNodeNameLength)
            {
                errors.Add(new ImportErrorDto(row.Row, code, $"سطر {FaDigits.Of(row.Row)}: نام گره حداکثر {FaDigits.Of(MaxNodeNameLength)} نویسه است."));
            }

            if (!string.IsNullOrWhiteSpace(row.ManagerPostName))
            {
                var title = row.ManagerPostName.Trim();
                if (positions.FirstOrDefault(p => forCompany.Contains(p.Id) && Normalize(p.Title) == Normalize(title)) is { } position)
                {
                    managers[row.Row] = position;
                }
                else
                {
                    errors.Add(new ImportErrorDto(row.Row, code, $"سطر {FaDigits.Of(row.Row)}: سمت مدیر «{title}» برای این شرکت تعریف نشده است."));
                }
            }
        }

        var items = new List<ImportItem>();
        if (parsed.IsValid)
        {
            void Walk(IEnumerable<ParsedStructureNode> nodes, string? parentCode, int depth)
            {
                foreach (var node in nodes)
                {
                    if (node.Code == ParentSentinel)
                    {
                        Walk(node.Children, null, depth);
                        continue;
                    }

                    var type = types.First(t => t.Code == (node.TypeCode ?? PlainUnitCode));
                    var manager = managers.GetValueOrDefault(node.Row);
                    items.Add(new ImportItem(node.Row, node.Code, node.Name, parentCode, type.Id, type.Name, manager?.Id, manager?.Title, depth));
                    Walk(node.Children, node.Code, depth + 1);
                }
            }

            Walk(parsed.Roots, null, 0);

        }

        // Nesting needs «what may sit under what» rules, as in the node API.
        if (rows.Any(row => !Blank(row) && !string.IsNullOrWhiteSpace(row.ParentCode)) || (parent is not null && rows.Any(row => !Blank(row))))
        {
            if (state.Rules.Count == 0)
            {
                errors.Add(new ImportErrorDto(null, null, "هنوز قاعده‌ای برای «چه زیر چه» تعریف نشده؛ گره فقط در سطح اول شرکت ممکن است."));
            }
        }

        return Result.Success(new ImportPlan(companyId, parent?.Id, items, errors.OrderBy(e => e.Row ?? int.MaxValue).ToList()));
    }

    /// <summary>Makes the plan's nodes (all or nothing), then their automatic links. Returns the nodes with their manager's position, and the links left to choose.</summary>
    internal async Task<Result<(List<(OrganizationUnit Unit, Guid? ManagerPositionId)> Made, List<AutoLinkChoiceDto> Choices, int Links)>> ImportNodesAsync(
        Guid tenantId, ImportTargetRequest target, IReadOnlyList<ExcelStructureRow> rows, CancellationToken cancellationToken)
    {
        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<(List<(OrganizationUnit, Guid?)>, List<AutoLinkChoiceDto>, int)>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var planned = await PlanImportAsync(tenantId, state, target, rows, cancellationToken);
        if (!planned.IsSuccess)
        {
            return Result.Failure<(List<(OrganizationUnit, Guid?)>, List<AutoLinkChoiceDto>, int)>(planned.Error);
        }

        var plan = planned.Value!;
        if (plan.Errors.Count > 0)
        {
            return Result.Failure<(List<(OrganizationUnit, Guid?)>, List<AutoLinkChoiceDto>, int)>(
                Error.Validation($"فایل {FaDigits.Of(plan.Errors.Count)} خطا دارد و هیچ گره‌ای ساخته نشد؛ پیش‌نمایش را ببینید."));
        }

        var made = new List<(OrganizationUnit Unit, Guid? ManagerPositionId)>();
        var byCode = new Dictionary<string, OrganizationUnit>(StringComparer.OrdinalIgnoreCase);
        var nextTop = NextSortOrder(state, plan.ParentNodeId, plan.ParentNodeId is null ? plan.CompanyId : null);
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items)
        {
            var unit = new OrganizationUnit(Guid.NewGuid(), tenantId, item.Name);
            unit.SetNodeCode(item.Code);
            unit.SetNodeType(item.TypeId);
            if (item.ParentCode is { } parentCode)
            {
                unit.SetParent(byCode[parentCode].Id);
                unit.SetSortOrder(order[parentCode] = order.GetValueOrDefault(parentCode, -1) + 1);
            }
            else
            {
                unit.SetParent(plan.ParentNodeId);
                unit.SetSortOrder(nextTop++);
            }

            await units.AddAsync(unit, cancellationToken);
            if (unit.ParentUnitId is null)
            {
                await repository.AddCompanyUnitAsync(new CompanyUnit(Guid.NewGuid(), tenantId, plan.CompanyId, unit.Id), cancellationToken);
            }

            byCode[item.Code] = unit;
            made.Add((unit, item.ManagerPositionId));
        }

        await LogAsync(tenantId, "Company", plan.CompanyId, "import", new { parent = plan.ParentNodeId, nodes = made.Count }, cancellationToken);
        if (!await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken))
        {
            return Result.Failure<(List<(OrganizationUnit, Guid?)>, List<AutoLinkChoiceDto>, int)>(Busy());
        }

        // The automatic links, as when a node is made: one target linked, several left to choose.
        var after = await LoadAsync(tenantId, cancellationToken);
        var choices = new List<AutoLinkChoiceDto>();
        var links = 0;
        foreach (var (unit, _) in made)
        {
            var (auto, some) = AutoLinksFor(after, after.Units[unit.Id], plan.CompanyId);
            foreach (var link in auto)
            {
                await repository.AddOrgLinkAsync(new OrgLink(Guid.NewGuid(), tenantId, unit.Id, link.ToNodeId, link.LinkTypeId), cancellationToken);
                links++;
            }

            choices.AddRange(some);
        }

        if (links > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success((made, choices, links));
    }
}
