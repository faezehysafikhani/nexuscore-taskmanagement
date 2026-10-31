using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// The nodes of the dynamic structure (units under units), their types and «what may sit under
/// what». Every change is made in the organization's turn (IOrgStructureWriteLock), checked against
/// the data as it is then (loops, the rules, the company), saved with the node's version
/// (RowVersion) and written to the change log. Without any «what may sit under what» rule a node
/// is only made and moved at the top of its company (the flat structure). A user kept to their own company
/// (LimitToOwnCompany) sees and changes only its nodes.
/// </summary>
public interface IOrgNodeService
{
    Task<Result<IReadOnlyList<OrgNodeDto>>> ListNodesAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> GetNodeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> CreateNodeAsync(Guid tenantId, CreateNodeRequest request, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> UpdateNodeAsync(Guid tenantId, Guid id, UpdateNodeRequest request, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> SetManagerAsync(Guid tenantId, Guid id, SetNodeManagerRequest request, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> SetDetailsAsync(Guid tenantId, Guid id, SetNodeDetailsRequest request, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> MoveNodeAsync(Guid tenantId, Guid id, MoveNodeRequest request, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> ReorderNodeAsync(Guid tenantId, Guid id, ReorderNodeRequest request, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> DeactivateNodeAsync(Guid tenantId, Guid id, NodeVersionRequest request, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> ActivateNodeAsync(Guid tenantId, Guid id, NodeVersionRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<NodeSearchResultDto>>> SearchAsync(Guid tenantId, string? query, Guid? companyId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<NodePathItemDto>>> GetAncestorsAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<NodeTypeDto>>> ListNodeTypesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<NodeTypeDto>> CreateNodeTypeAsync(Guid tenantId, CreateNodeTypeRequest request, CancellationToken cancellationToken);
    Task<Result<NodeTypeDto>> UpdateNodeTypeAsync(Guid tenantId, Guid id, UpdateNodeTypeRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteNodeTypeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<Result<NodeTypeRulesDto>> GetRulesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<NodeTypeRulesDto>> SetRulesAsync(Guid tenantId, SetNodeTypeRulesRequest request, CancellationToken cancellationToken);
}

public sealed partial class OrgNodeService(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    IOrganizationUnitRepository units,
    IOrganizationUnitOfWork unitsUnitOfWork,
    IUserDirectory users,
    IOrgStructureWriteLock? writeLock = null,
    CompanyReachScope? companyReach = null,
    ICurrentUserContext? currentUser = null,
    TimeProvider? clock = null) : IOrgNodeService
{
    public const int MaxNodeNameLength = 160;
    public const int MaxTypeNameLength = 120;
    private const int MaxSearchResults = 50;

    private readonly IOrgStructureWriteLock _lock = writeLock ?? new InProcessOrgStructureWriteLock();

    // ---------------------------------------------------------------- reading

    public async Task<Result<IReadOnlyList<OrgNodeDto>>> ListNodesAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<OrgNodeDto>>(state.Units.Values
            .Where(unit => Sees(state, mine, unit.Id))
            .Where(unit => companyId is null || state.Graph.CompanyOfUnit(unit.Id) == companyId)
            .OrderBy(unit => Depth(state, unit.Id)).ThenBy(unit => unit.SortOrder).ThenBy(unit => unit.Name)
            .Select(unit => ToDto(state, unit))
            .ToList());
    }

    public async Task<Result<OrgNodeDto>> GetNodeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        return state.Units.TryGetValue(id, out var unit) && Sees(state, await MyCompaniesAsync(tenantId, cancellationToken), id)
            ? Result.Success(ToDto(state, unit))
            : Result.Failure<OrgNodeDto>(NodeNotFound());
    }

    public async Task<Result<IReadOnlyList<NodePathItemDto>>> GetAncestorsAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        if (!state.Units.ContainsKey(id) || !Sees(state, await MyCompaniesAsync(tenantId, cancellationToken), id))
        {
            return Result.Failure<IReadOnlyList<NodePathItemDto>>(NodeNotFound());
        }

        return Result.Success(PathOf(state, id));
    }

    public async Task<Result<IReadOnlyList<NodeSearchResultDto>>> SearchAsync(Guid tenantId, string? query, Guid? companyId, CancellationToken cancellationToken)
    {
        var text = Normalize(query);
        if (text.Length == 0)
        {
            return Result.Success<IReadOnlyList<NodeSearchResultDto>>([]);
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        var visible = state.Units.Values
            .Where(unit => Sees(state, mine, unit.Id))
            .Where(unit => companyId is null || state.Graph.CompanyOfUnit(unit.Id) == companyId)
            .ToList();

        // Who sits where: each node's manager and the holders of its posts.
        var peopleIds = visible.Select(unit => state.Graph.Node(unit.Id)?.ManagerUserId)
            .Concat(state.Holdings.Select(h => (Guid?)h.UserId))
            .OfType<Guid>().Distinct().ToList();
        var names = (await users.GetUsersAsync(peopleIds, cancellationToken)).ToDictionary(user => user.Id, user => Normalize(user.DisplayName));
        var seatsByUnit = state.Seats.Where(seat => seat.UnitId is not null).ToLookup(seat => seat.UnitId!.Value);
        var holderOf = state.Holdings.GroupBy(h => h.UnitPositionId).ToDictionary(group => group.Key, group => group.First().UserId);

        var results = new List<NodeSearchResultDto>();
        foreach (var unit in visible.OrderBy(unit => Depth(state, unit.Id)).ThenBy(unit => unit.SortOrder).ThenBy(unit => unit.Name))
        {
            string? matched = null;
            if (Normalize(unit.Name).Contains(text, StringComparison.Ordinal))
            {
                matched = "name";
            }
            else if (seatsByUnit[unit.Id].Any(seat => Normalize(seat.Code) == text))
            {
                matched = "code";
            }
            else if ((state.Graph.Node(unit.Id)?.ManagerUserId is { } manager && names.GetValueOrDefault(manager)?.Contains(text, StringComparison.Ordinal) == true)
                     || seatsByUnit[unit.Id].Any(seat => holderOf.TryGetValue(seat.Id, out var holder)
                                                         && names.GetValueOrDefault(holder)?.Contains(text, StringComparison.Ordinal) == true))
            {
                matched = "person";
            }

            if (matched is not null)
            {
                var company = state.Graph.CompanyOfUnit(unit.Id) is { } companyOf ? state.Companies.GetValueOrDefault(companyOf)?.Name : null;
                results.Add(new NodeSearchResultDto(ToDto(state, unit), PathOf(state, unit.Id), matched, company));
                if (results.Count == MaxSearchResults)
                {
                    break;
                }
            }
        }

        return Result.Success<IReadOnlyList<NodeSearchResultDto>>(results);
    }

    // ---------------------------------------------------------------- changing nodes

    public async Task<Result<OrgNodeDto>> CreateNodeAsync(Guid tenantId, CreateNodeRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (NameProblem(name) is { } invalid)
        {
            return Result.Failure<OrgNodeDto>(invalid);
        }

        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<OrgNodeDto>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);

        var typeId = request.NodeTypeId ?? state.NodeTypes.Values.FirstOrDefault(type => type.Code == PlainUnitCode)?.Id;
        if (request.NodeTypeId is { } askedType && (!state.NodeTypes.TryGetValue(askedType, out var type) || !type.IsActive))
        {
            return Result.Failure<OrgNodeDto>(Error.Validation("جزء چارت پیدا نشد یا غیرفعال است."));
        }

        Guid? companyId = request.CompanyId;
        if (request.ParentNodeId is { } parentId)
        {
            if (!state.Units.TryGetValue(parentId, out var parent) || !Sees(state, mine, parentId))
            {
                return Result.Failure<OrgNodeDto>(Error.NotFound("گره‌ی والد پیدا نشد."));
            }

            if (!parent.IsActive)
            {
                return Result.Failure<OrgNodeDto>(Error.Validation($"گره‌ی والد «{parent.Name}» غیرفعال است."));
            }

            var parentCompany = state.Graph.CompanyOfUnit(parentId);
            if (companyId is not null && companyId != parentCompany)
            {
                return Result.Failure<OrgNodeDto>(Error.Validation("شرکت گره‌ی زیرمجموعه همان شرکت والد آن است."));
            }

            companyId = null; // only a top node is linked to its company
        }
        else if (companyId is { } topCompany && CompanyProblem(state, mine, topCompany) is { } companyProblem)
        {
            return Result.Failure<OrgNodeDto>(companyProblem);
        }

        var nodeCompanyId = request.ParentNodeId is { } p0 ? state.Graph.CompanyOfUnit(p0) : companyId;
        if (NodeCodeProblem(state, request.Code, nodeCompanyId, null) is { } codeProblem)
        {
            return Result.Failure<OrgNodeDto>(codeProblem);
        }

        if (SiblingNameProblem(state, name, request.ParentNodeId, request.ParentNodeId is null ? companyId : null, null) is { } sameName)
        {
            return Result.Failure<OrgNodeDto>(sameName);
        }

        var unit = new OrganizationUnit(Guid.NewGuid(), tenantId, name);
        unit.SetNodeCode(request.Code);
        unit.SetParent(request.ParentNodeId);
        unit.SetNodeType(typeId);
        unit.SetSortOrder(NextSortOrder(state, request.ParentNodeId, request.ParentNodeId is null ? companyId : null));

        if (RulesProblem(state, unit, request.ParentNodeId, typeId, isNew: true) is { } ruleProblem)
        {
            return Result.Failure<OrgNodeDto>(ruleProblem);
        }

        await units.AddAsync(unit, cancellationToken);
        if (companyId is { } linkTo)
        {
            await repository.AddCompanyUnitAsync(new CompanyUnit(Guid.NewGuid(), tenantId, linkTo, unit.Id), cancellationToken);
        }

        // Its automatic links: one target, linked now; several, left for the user to choose.
        var nodeCompany = request.ParentNodeId is { } under ? state.Graph.CompanyOfUnit(under) : companyId;
        var (autoLinks, choices) = AutoLinksFor(state, unit, nodeCompany);
        foreach (var link in autoLinks)
        {
            await repository.AddOrgLinkAsync(new OrgLink(Guid.NewGuid(), tenantId, unit.Id, link.ToNodeId, link.LinkTypeId), cancellationToken);
        }

        await LogAsync(tenantId, "Node", unit.Id, "create", new { name, parent = request.ParentNodeId, company = companyId, type = typeId, autoLinks = autoLinks.Select(l => l.ToNodeId) }, cancellationToken);
        if (!await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken))
        {
            return Result.Failure<OrgNodeDto>(ChangedMeanwhile());
        }

        var created = await GetNodeAsync(tenantId, unit.Id, cancellationToken);
        return created.IsSuccess && choices.Count > 0 ? Result.Success(created.Value! with { AutoLinkChoices = choices }) : created;
    }

    public Task<Result<OrgNodeDto>> UpdateNodeAsync(Guid tenantId, Guid id, UpdateNodeRequest request, CancellationToken cancellationToken) =>
        ChangeNodeAsync(tenantId, id, request.RowVersion, "update", (state, unit) =>
        {
            var name = request.Name?.Trim() ?? string.Empty;
            if (NameProblem(name) is { } invalid)
            {
                return Task.FromResult<(Error?, object?)>((invalid, null));
            }

            var typeId = request.NodeTypeId ?? unit.NodeTypeId;
            if (request.NodeTypeId is { } askedType && askedType != unit.NodeTypeId
                && (!state.NodeTypes.TryGetValue(askedType, out var type) || !type.IsActive))
            {
                return Task.FromResult<(Error?, object?)>((Error.Validation("جزء چارت پیدا نشد یا غیرفعال است."), null));
            }

            if (typeId != unit.NodeTypeId && unit.NodeTypeId is { } currentTypeId
                && state.NodeTypes.TryGetValue(currentTypeId, out var currentType) && OrgNodeKinds.IsKindTypeCode(currentType.Code))
            {
                return Task.FromResult<(Error?, object?)>((Error.Validation("نوع گره بعد از ساخت قابل تغییر نیست."), null));
            }

            if (typeId != unit.NodeTypeId && RulesProblem(state, unit, unit.ParentUnitId, typeId, isNew: false) is { } ruleProblem)
            {
                return Task.FromResult<(Error?, object?)>((ruleProblem, null));
            }

            if (Normalize(name) != Normalize(unit.Name)
                && SiblingNameProblem(state, name, state.Graph.ParentOf(unit.Id), state.Graph.ParentOf(unit.Id) is null ? state.Graph.CompanyOfUnit(unit.Id) : null, unit.Id) is { } sameName)
            {
                return Task.FromResult<(Error?, object?)>((sameName, null));
            }

            var before = new { name = unit.Name, type = unit.NodeTypeId };
            unit.Update(name, unit.ManagerUserId, unit.IsActive);
            unit.SetNodeType(typeId);
            return Task.FromResult<(Error?, object?)>((null, new { before, after = new { name, type = typeId } }));
        }, cancellationToken);

    public Task<Result<OrgNodeDto>> SetDetailsAsync(Guid tenantId, Guid id, SetNodeDetailsRequest request, CancellationToken cancellationToken) =>
        ChangeNodeAsync(tenantId, id, request.RowVersion, "details", (state, unit) =>
        {
            if (NodeCodeProblem(state, request.Code, state.Graph.CompanyOfUnit(unit.Id), unit.Id) is { } codeProblem)
            {
                return Task.FromResult<(Error?, object?)>((codeProblem, null));
            }

            if (DetailsProblem(request) is { } invalid)
            {
                return Task.FromResult<(Error?, object?)>((invalid, null));
            }

            var before = new { code = unit.NodeCode, unit.LatinName, unit.CorrespondenceTitle, unit.Phone, unit.Address };
            unit.SetNodeCode(request.Code);
            unit.SetDetails(request.LatinName, request.CorrespondenceTitle, request.Phone, request.Address);
            return Task.FromResult<(Error?, object?)>((null, new { before, after = new { code = unit.NodeCode, unit.LatinName, unit.CorrespondenceTitle, unit.Phone, unit.Address } }));
        }, cancellationToken);

    public Task<Result<OrgNodeDto>> SetManagerAsync(Guid tenantId, Guid id, SetNodeManagerRequest request, CancellationToken cancellationToken) =>
        ChangeNodeAsync(tenantId, id, request.RowVersion, "manager", async (state, unit) =>
        {
            if (request.ManagerUserId is { } managerId)
            {
                var manager = (await users.GetUsersAsync([managerId], cancellationToken)).FirstOrDefault();
                if (manager is null || manager.TenantId != tenantId || !manager.IsActive)
                {
                    return (Error.Validation("مدیر باید از کاربران فعال همین سازمان باشد."), null);
                }
            }

            var before = unit.ManagerUserId;
            unit.Update(unit.Name, request.ManagerUserId, unit.IsActive);
            return (null, new { before, after = request.ManagerUserId });
        }, cancellationToken);

    public Task<Result<OrgNodeDto>> MoveNodeAsync(Guid tenantId, Guid id, MoveNodeRequest request, CancellationToken cancellationToken) =>
        ChangeNodeAsync(tenantId, id, request.RowVersion, "move", async (state, unit) =>
        {
            var mine = await MyCompaniesAsync(tenantId, cancellationToken);
            var subtree = state.Graph.SubtreeOf(unit.Id).ToHashSet();
            Guid? newCompany;
            if (request.ParentNodeId is { } parentId)
            {
                if (!state.Units.TryGetValue(parentId, out var parent) || !Sees(state, mine, parentId))
                {
                    return (Error.NotFound("گره‌ی مقصد پیدا نشد."), null);
                }

                if (subtree.Contains(parentId))
                {
                    return (Error.Validation($"گره را نمی‌شود زیر خودش یا زیرمجموعه‌ی خودش برد («{parent.Name}»)."), null);
                }

                if (!parent.IsActive)
                {
                    return (Error.Validation($"گره‌ی مقصد «{parent.Name}» غیرفعال است."), null);
                }

                newCompany = state.Graph.CompanyOfUnit(parentId);
                if (request.CompanyId is not null && request.CompanyId != newCompany)
                {
                    return (Error.Validation("شرکت گره‌ی زیرمجموعه همان شرکت والد آن است."), null);
                }
            }
            else
            {
                newCompany = request.CompanyId ?? state.Graph.CompanyOfUnit(unit.Id);
                if (request.CompanyId is { } company && CompanyProblem(state, mine, company) is { } companyProblem)
                {
                    return (companyProblem, null);
                }
            }

            // Below it, only it may be linked to a company (the links of nodes under it would be lost).
            if (state.Links.Any(link => link.UnitId != unit.Id && subtree.Contains(link.UnitId)))
            {
                return (Error.Conflict("گره‌هایی زیر این گره مستقیم به شرکت وصل‌اند؛ اول آن‌ها را جدا کنید."), null);
            }

            var oldCompany = state.Graph.CompanyOfUnit(unit.Id);
            if (newCompany != oldCompany)
            {
                // Another company: its people and posts would have to change company too, which this does not do.
                var people = state.Assignments.Count(a => a.UnitId is { } u && subtree.Contains(u));
                var posts = state.Seats.Count(seat => seat.UnitId is { } u && subtree.Contains(u));
                if (people > 0 || posts > 0)
                {
                    return (Error.Conflict($"این گره و زیرمجموعه‌اش {FaDigits.Of(posts)} پست و {FaDigits.Of(people)} نفر دارند؛ برای بردن به شرکت دیگر اول آن‌ها را جابه‌جا کنید."), null);
                }

                // Node codes are unique in a company: none of the branch's may already be used there.
                foreach (var moving in subtree.Select(id => state.Units[id]).Where(u => u.NodeCode is not null))
                {
                    if (state.Units.Values.Any(other => !subtree.Contains(other.Id) && other.NodeCode is { } c
                                                        && Normalize(c) == Normalize(moving.NodeCode) && state.Graph.CompanyOfUnit(other.Id) == newCompany))
                    {
                        return (Error.Conflict($"کد «{moving.NodeCode}» (گره‌ی «{moving.Name}») در شرکت مقصد هست."), null);
                    }
                }
            }

            if (RulesProblem(state, unit, request.ParentNodeId, unit.NodeTypeId, isNew: false) is { } ruleProblem)
            {
                return (ruleProblem, null);
            }

            if (SiblingNameProblem(state, unit.Name, request.ParentNodeId, request.ParentNodeId is null ? newCompany : null, unit.Id) is { } sameName)
            {
                return (sameName, null);
            }

            var before = new { parent = unit.ParentUnitId, company = oldCompany };
            var link = state.Links.FirstOrDefault(l => l.UnitId == unit.Id);
            if (request.ParentNodeId is not null || newCompany is null)
            {
                if (link is not null)
                {
                    repository.RemoveCompanyUnit(link);
                }
            }
            else if (link is null)
            {
                await repository.AddCompanyUnitAsync(new CompanyUnit(Guid.NewGuid(), tenantId, newCompany.Value, unit.Id), cancellationToken);
            }
            else if (link.CompanyId != newCompany)
            {
                link.MoveTo(newCompany.Value);
            }

            unit.SetParent(request.ParentNodeId);
            unit.SetSortOrder(NextSortOrder(state, request.ParentNodeId, request.ParentNodeId is null ? newCompany : null, except: unit.Id));
            return (null, new { before, after = new { parent = request.ParentNodeId, company = newCompany } });
        }, cancellationToken);

    public Task<Result<OrgNodeDto>> ReorderNodeAsync(Guid tenantId, Guid id, ReorderNodeRequest request, CancellationToken cancellationToken) =>
        ChangeNodeAsync(tenantId, id, request.RowVersion, "reorder", (state, unit) =>
        {
            var siblings = SiblingsOf(state, unit).Where(other => other.Id != unit.Id).ToList();
            var position = Math.Clamp(request.Position, 0, siblings.Count);
            siblings.Insert(position, unit);
            for (var i = 0; i < siblings.Count; i++)
            {
                siblings[i].SetSortOrder(i);
            }

            return Task.FromResult<(Error?, object?)>((null, new { position }));
        }, cancellationToken);

    public Task<Result<OrgNodeDto>> DeactivateNodeAsync(Guid tenantId, Guid id, NodeVersionRequest request, CancellationToken cancellationToken) =>
        ChangeNodeAsync(tenantId, id, request.RowVersion, "deactivate", (state, unit) =>
        {
            if (!unit.IsActive)
            {
                return Task.FromResult<(Error?, object?)>((null, null));
            }

            // What still depends on it: it is refused, with the list, rather than leaving them behind.
            var blockers = new List<string>();
            var activeChildren = state.Graph.ChildrenOf(unit.Id).Select(childId => state.Units.GetValueOrDefault(childId)).OfType<OrganizationUnit>()
                .Where(child => child.IsActive).Select(child => child.Name).ToList();
            if (activeChildren.Count > 0)
            {
                blockers.Add($"گره‌های فعال زیر آن: {string.Join("، ", activeChildren)}");
            }

            var posts = state.Seats.Count(seat => seat.UnitId == unit.Id);
            if (posts > 0)
            {
                blockers.Add($"{FaDigits.Of(posts)} پست");
            }

            var people = state.Assignments.Count(a => a.UnitId == unit.Id);
            if (people > 0)
            {
                blockers.Add($"{FaDigits.Of(people)} نفر که در آن قرار دارند");
            }

            if (blockers.Count > 0)
            {
                return Task.FromResult<(Error?, object?)>((Error.Conflict($"«{unit.Name}» را نمی‌شود غیرفعال کرد؛ اول این‌ها را جابه‌جا کنید: {string.Join(" · ", blockers)}."), null));
            }

            unit.Update(unit.Name, unit.ManagerUserId, isActive: false);
            return Task.FromResult<(Error?, object?)>((null, new { }));
        }, cancellationToken);

    public Task<Result<OrgNodeDto>> ActivateNodeAsync(Guid tenantId, Guid id, NodeVersionRequest request, CancellationToken cancellationToken) =>
        ChangeNodeAsync(tenantId, id, request.RowVersion, "activate", (state, unit) =>
        {
            if (unit.ParentUnitId is { } parentId && state.Units.TryGetValue(parentId, out var parent) && !parent.IsActive)
            {
                return Task.FromResult<(Error?, object?)>((Error.Validation($"اول گره‌ی بالای آن («{parent.Name}») را فعال کنید."), null));
            }

            unit.Update(unit.Name, unit.ManagerUserId, isActive: true);
            return Task.FromResult<(Error?, object?)>((null, new { }));
        }, cancellationToken);

    /// <summary>
    /// The common steps of a change to one node: the organization's turn, the node (visible to the
    /// caller), its version, the change itself (an error, or what to log), the log and the save.
    /// A change that returns no log details changed nothing.
    /// </summary>
    private async Task<Result<OrgNodeDto>> ChangeNodeAsync(
        Guid tenantId,
        Guid id,
        string? rowVersion,
        string action,
        Func<NodeState, OrganizationUnit, Task<(Error? Error, object? Details)>> change,
        CancellationToken cancellationToken)
    {
        byte[]? expected = null;
        if (!string.IsNullOrWhiteSpace(rowVersion))
        {
            try
            {
                expected = Convert.FromBase64String(rowVersion);
            }
            catch (FormatException)
            {
                return Result.Failure<OrgNodeDto>(Error.Validation("نسخه‌ی گره (rowVersion) درست نیست."));
            }
        }

        await using (var turn = await _lock.BeginAsync(tenantId, cancellationToken))
        {
            if (turn is null)
            {
                return Result.Failure<OrgNodeDto>(Busy());
            }

            var state = await LoadAsync(tenantId, cancellationToken);
            if (!state.Units.TryGetValue(id, out var unit) || !Sees(state, await MyCompaniesAsync(tenantId, cancellationToken), id))
            {
                return Result.Failure<OrgNodeDto>(NodeNotFound());
            }

            if (expected is not null && !expected.AsSpan().SequenceEqual(unit.RowVersion ?? []))
            {
                return Result.Failure<OrgNodeDto>(ChangedMeanwhile());
            }

            var (error, details) = await change(state, unit);
            if (error is not null)
            {
                return Result.Failure<OrgNodeDto>(error);
            }

            if (details is not null)
            {
                await LogAsync(tenantId, "Node", unit.Id, action, details, cancellationToken);
                if (!await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken))
                {
                    return Result.Failure<OrgNodeDto>(ChangedMeanwhile());
                }
            }
        }

        return await GetNodeAsync(tenantId, id, cancellationToken);
    }

    // ---------------------------------------------------------------- node types and rules

    public async Task<Result<IReadOnlyList<NodeTypeDto>>> ListNodeTypesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<NodeTypeDto>>(state.NodeTypes.Values
            .OrderBy(type => type.SortOrder).ThenBy(type => type.Name)
            .Select(type => ToDto(state, type))
            .ToList());
    }

    public async Task<Result<NodeTypeDto>> CreateNodeTypeAsync(Guid tenantId, CreateNodeTypeRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (TypeProblem(name, request.Color, request.Icon) is { } invalid)
        {
            return Result.Failure<NodeTypeDto>(invalid);
        }

        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<NodeTypeDto>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        if (state.NodeTypes.Values.Any(type => string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<NodeTypeDto>(Error.Conflict($"نوعی با نام «{name}» وجود دارد."));
        }

        var code = request.Code?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
        {
            var n = state.NodeTypes.Count + 1;
            while (state.NodeTypes.Values.Any(type => type.Code == $"type-{n}"))
            {
                n++;
            }

            code = $"type-{n}";
        }
        else if (!CodePattern().IsMatch(code) || code.Length > 40)
        {
            return Result.Failure<NodeTypeDto>(Error.Validation("کد نوع فقط حروف کوچک لاتین، رقم و خط تیره است (حداکثر ۴۰)."));
        }
        else if (state.NodeTypes.Values.Any(type => type.Code == code))
        {
            return Result.Failure<NodeTypeDto>(Error.Conflict($"نوعی با کد «{code}» وجود دارد."));
        }

        var nodeType = new NodeType(Guid.NewGuid(), tenantId, code, name, request.IsDomainBoundary,
            state.NodeTypes.Count == 0 ? 0 : state.NodeTypes.Values.Max(type => type.SortOrder) + 1);
        nodeType.SetStyle(request.Color ?? NodeType.DefaultColor, request.Icon ?? NodeType.DefaultIcon);
        await repository.AddNodeTypeAsync(nodeType, cancellationToken);
        await LogAsync(tenantId, "NodeType", nodeType.Id, "create", new { code, name }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success(ToDto(state, nodeType));
    }

    public async Task<Result<NodeTypeDto>> UpdateNodeTypeAsync(Guid tenantId, Guid id, UpdateNodeTypeRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (TypeProblem(name, request.Color, request.Icon) is { } invalid)
        {
            return Result.Failure<NodeTypeDto>(invalid);
        }

        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<NodeTypeDto>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        if (!state.NodeTypes.TryGetValue(id, out var nodeType))
        {
            return Result.Failure<NodeTypeDto>(Error.NotFound("جزء چارت پیدا نشد."));
        }

        if (state.NodeTypes.Values.Any(type => type.Id != id && string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<NodeTypeDto>(Error.Conflict($"نوعی با نام «{name}» وجود دارد."));
        }

        var before = new { nodeType.Name, nodeType.Color, nodeType.Icon, nodeType.IsDomainBoundary, nodeType.SortOrder, nodeType.IsActive };
        nodeType.Update(name, request.IsDomainBoundary, request.SortOrder, request.IsActive);
        nodeType.SetStyle(request.Color ?? nodeType.Color, request.Icon ?? nodeType.Icon);
        await LogAsync(tenantId, "NodeType", id, "update", new { before, after = new { name, request.Color, request.Icon, request.IsDomainBoundary, request.SortOrder, request.IsActive } }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success(ToDto(state, nodeType));
    }

    public async Task<Result> DeleteNodeTypeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        if (!state.NodeTypes.TryGetValue(id, out var nodeType))
        {
            return Result.Failure(Error.NotFound("جزء چارت پیدا نشد."));
        }

        var used = state.Units.Values.Count(unit => unit.NodeTypeId == id);
        var inRules = state.Rules.Any(rule => rule.ChildNodeTypeId == id || rule.ParentNodeTypeId == id);
        var elsewhere = (await repository.ListNodeTypePostTemplatesAsync(tenantId, cancellationToken)).Any(t => t.NodeTypeId == id)
                        || (await repository.ListAutoLinkRulesAsync(tenantId, cancellationToken)).Any(r => r.SourceNodeTypeId == id || r.TargetNodeTypeId == id);
        if (used > 0 || inRules || elsewhere)
        {
            return Result.Failure(Error.Conflict(used > 0
                ? $"{FaDigits.Of(used)} گره از نوع «{nodeType.Name}» است؛ به‌جای حذف، آن را غیرفعال کنید."
                : $"نوع «{nodeType.Name}» در قاعده‌ها به‌کار رفته است؛ اول آن قاعده‌ها را بردارید."));
        }

        repository.RemoveNodeType(nodeType);
        await LogAsync(tenantId, "NodeType", id, "delete", new { nodeType.Code, nodeType.Name }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<NodeTypeRulesDto>> GetRulesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        var rules = state.Rules.Select(rule => new NodeTypeRuleDto(rule.ParentNodeTypeId, rule.ChildNodeTypeId)).ToList();
        return Result.Success(new NodeTypeRulesDto(rules, Conflicts(state, rules, await MyCompaniesAsync(tenantId, cancellationToken))));
    }

    /// <summary>Replaces the rules. Nodes that no longer match are listed, never changed or removed.</summary>
    public async Task<Result<NodeTypeRulesDto>> SetRulesAsync(Guid tenantId, SetNodeTypeRulesRequest request, CancellationToken cancellationToken)
    {
        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<NodeTypeRulesDto>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var wanted = (request.Rules ?? []).Distinct().ToList();
        if (wanted.Any(rule => !state.NodeTypes.ContainsKey(rule.ChildNodeTypeId)
                               || (rule.ParentNodeTypeId is { } parent && !state.NodeTypes.ContainsKey(parent))))
        {
            return Result.Failure<NodeTypeRulesDto>(Error.Validation("قاعده به نوعی اشاره می‌کند که وجود ندارد."));
        }

        var current = state.Rules.ToList();
        foreach (var rule in current.Where(rule => !wanted.Contains(new NodeTypeRuleDto(rule.ParentNodeTypeId, rule.ChildNodeTypeId))))
        {
            repository.RemoveNodeTypeRule(rule);
        }

        foreach (var rule in wanted.Where(rule => !current.Any(c => c.ParentNodeTypeId == rule.ParentNodeTypeId && c.ChildNodeTypeId == rule.ChildNodeTypeId)))
        {
            await repository.AddNodeTypeRuleAsync(new NodeTypeRule(Guid.NewGuid(), tenantId, rule.ParentNodeTypeId, rule.ChildNodeTypeId), cancellationToken);
        }

        await LogAsync(tenantId, "NodeTypeRules", tenantId, "set",
            new { before = current.Select(rule => new { rule.ParentNodeTypeId, rule.ChildNodeTypeId }), after = wanted }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success(new NodeTypeRulesDto(wanted, Conflicts(state, wanted, await MyCompaniesAsync(tenantId, cancellationToken))));
    }

    // ---------------------------------------------------------------- checks

    /// <summary>The «plain unit» type every existing unit got (upgrade-10); a new node without a type is of it.</summary>
    public const string PlainUnitCode = "unit";

    /// <summary>
    /// Whether a node of this type may sit there, asked of the structure validator (M3) on the
    /// whole structure as it would be. No rules at all: only the top (a flat structure).
    /// For a type change, its children must still be allowed under it too.
    /// </summary>
    private static Error? RulesProblem(NodeState state, OrganizationUnit unit, Guid? parentId, Guid? typeId, bool isNew)
    {
        if (state.Rules.Count == 0)
        {
            return parentId is null
                ? null
                : Error.Validation("هنوز قاعده‌ای برای «سلسله‌مراتب مجاز» تعریف نشده؛ گره فقط در سطح اول شرکت ممکن است.");
        }

        string Key(Guid id) => id.ToString("N");
        var inputs = state.Units.Values
            .Where(other => other.Id != unit.Id)
            .Select(other => new StructureNodeInput(Key(other.Id), other.Name, other.ParentUnitId is { } p ? Key(p) : null, other.NodeTypeId is { } t ? Key(t) : null))
            .Append(new StructureNodeInput(Key(unit.Id), unit.Name, parentId is { } np ? Key(np) : null, typeId is { } nt ? Key(nt) : null))
            .ToList();
        var errors = OrgStructureValidator.Validate(
            inputs,
            state.NodeTypes.Keys.Select(Key).ToList(),
            state.Rules.Select(rule => new StructureTypeRule(rule.ParentNodeTypeId is { } p ? Key(p) : null, Key(rule.ChildNodeTypeId))).ToList());

        var children = isNew ? [] : state.Graph.ChildrenOf(unit.Id).Select(Key).ToHashSet();
        var problem = errors.FirstOrDefault(error => error.NodeCode == Key(unit.Id)
            || (error.Kind == StructureErrorKind.TypeNotAllowedHere && error.NodeCode is { } code && children.Contains(code)));
        if (problem is null)
        {
            return null;
        }

        string TypeName(Guid? id) => id is { } t && state.NodeTypes.TryGetValue(t, out var type) ? type.Name : "بی‌نوع";
        return problem.Kind switch
        {
            StructureErrorKind.Cycle => Error.Validation("این جابه‌جایی در ساختار حلقه می‌سازد."),
            StructureErrorKind.UnknownType => Error.Validation("جزء چارت پیدا نشد."),
            StructureErrorKind.TypeNotAllowedHere when problem.NodeCode == Key(unit.Id) => Error.Validation(parentId is { } pid && state.Units.TryGetValue(pid, out var parent)
                ? $"گره‌ی «{TypeName(typeId)}» را طبق قاعده‌ها نمی‌شود زیر «{parent.Name}» ({TypeName(parent.NodeTypeId)}) گذاشت."
                : $"گره‌ی «{TypeName(typeId)}» را طبق قاعده‌ها نمی‌شود در سطح اول شرکت گذاشت."),
            StructureErrorKind.TypeNotAllowedHere => Error.Validation($"با نوع «{TypeName(typeId)}» گره‌های زیر آن دیگر با قاعده‌ها جور نیستند."),
            _ => Error.Validation(problem.Message),
        };
    }

    /// <summary>Active nodes whose place does not match the rules (no rules: every node that is not at the top).</summary>
    private static IReadOnlyList<RuleConflictDto> Conflicts(NodeState state, IReadOnlyCollection<NodeTypeRuleDto> rules, IReadOnlySet<Guid>? mine)
    {
        var allowed = rules.Select(rule => (rule.ParentNodeTypeId, rule.ChildNodeTypeId)).ToHashSet();
        string? TypeName(Guid? id) => id is { } t && state.NodeTypes.TryGetValue(t, out var type) ? type.Name : null;
        return state.Units.Values
            .Where(unit => unit.IsActive && Sees(state, mine, unit.Id))
            .Where(unit =>
            {
                var parent = state.Graph.ParentOf(unit.Id) is { } p ? state.Units[p] : null;
                if (rules.Count == 0)
                {
                    return parent is not null;
                }

                // A node (or parent) with no type is not checked, as in the validator.
                return unit.NodeTypeId is { } type && (parent is null || parent.NodeTypeId is not null)
                       && !allowed.Contains((parent?.NodeTypeId, type));
            })
            .OrderBy(unit => unit.Name)
            .Select(unit =>
            {
                var parent = state.Graph.ParentOf(unit.Id) is { } p ? state.Units[p] : null;
                return new RuleConflictDto(unit.Id, unit.Name, TypeName(unit.NodeTypeId), parent?.Id, parent?.Name, TypeName(parent?.NodeTypeId));
            })
            .ToList();
    }

    private static Error? CompanyProblem(NodeState state, IReadOnlySet<Guid>? mine, Guid companyId)
    {
        if (!state.Companies.TryGetValue(companyId, out var company) || (mine is not null && !mine.Contains(companyId)))
        {
            return Error.NotFound("شرکت پیدا نشد.");
        }

        return company.IsActive ? null : Error.Validation($"شرکت «{company.Name}» غیرفعال است.");
    }

    public const int MaxNodeCodeLength = 40;

    /// <summary>A node code: at most 40 characters and not used by another node of the same company (compared loosely).</summary>
    private static Error? NodeCodeProblem(NodeState state, string? code, Guid? companyId, Guid? exceptId)
    {
        var text = code?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > MaxNodeCodeLength)
        {
            return Error.Validation($"کد گره حداکثر {FaDigits.Of(MaxNodeCodeLength)} نویسه است.");
        }

        var key = Normalize(text);
        return state.Units.Values.Any(other => other.Id != exceptId && other.NodeCode is { } c && Normalize(c) == key
                                               && state.Graph.CompanyOfUnit(other.Id) == companyId)
            ? Error.Conflict($"گره‌ی دیگری در همین شرکت کد «{text}» دارد.")
            : null;
    }

    private static Error? DetailsProblem(SetNodeDetailsRequest request) =>
        request.LatinName?.Trim().Length > 200 ? Error.Validation("نام لاتین حداکثر ۲۰۰ نویسه است.")
        : request.CorrespondenceTitle?.Trim().Length > 200 ? Error.Validation("عنوان مکاتباتی حداکثر ۲۰۰ نویسه است.")
        : request.Phone?.Trim().Length > 40 ? Error.Validation("شماره تماس حداکثر ۴۰ نویسه است.")
        : request.Address?.Trim().Length > 500 ? Error.Validation("نشانی حداکثر ۵۰۰ نویسه است.")
        : null;

    /// <summary>
    /// Two active nodes beside each other (the same parent, or the same company's top) may not have
    /// the same name (compared loosely). Only new names are checked: what is there already stays.
    /// </summary>
    private static Error? SiblingNameProblem(NodeState state, string name, Guid? parentId, Guid? companyId, Guid? exceptId)
    {
        var key = Normalize(name);
        var same = state.Units.Values.FirstOrDefault(other => other.Id != exceptId && other.IsActive && Normalize(other.Name) == key
            && (parentId is { } p ? other.ParentUnitId == p : state.Graph.ParentOf(other.Id) is null && state.Graph.CompanyOfUnit(other.Id) == companyId));
        return same is null ? null : Error.Conflict($"گره‌ی هم‌سطحی با نام «{same.Name}» همین‌جا هست؛ نام دیگری بدهید.");
    }

    private static Error? NameProblem(string name) =>
        name.Length == 0 ? Error.Validation("نام گره را وارد کنید.")
        : name.Length > MaxNodeNameLength ? Error.Validation($"نام گره حداکثر {FaDigits.Of(MaxNodeNameLength)} نویسه است.")
        : null;

    private static Error? TypeProblem(string name, string? color, string? icon) =>
        name.Length == 0 ? Error.Validation("نام نوع را وارد کنید.")
        : name.Length > MaxTypeNameLength ? Error.Validation($"نام نوع حداکثر {FaDigits.Of(MaxTypeNameLength)} نویسه است.")
        : color is not null && !ColorPattern().IsMatch(color.Trim()) ? Error.Validation("رنگ باید به شکل ‎#2563eb‎ باشد.")
        : icon is not null && (!CodePattern().IsMatch(icon.Trim()) || icon.Trim().Length > 40) ? Error.Validation("نام آیکون درست نیست.")
        : null;

    private static Error NodeNotFound() => Error.NotFound("گره پیدا نشد.");

    /// <summary>Code of the conflicts that a fresh read and a retry resolve (the screens offer «reload» only for these).</summary>
    public const string ChangedCode = "conflict.changed";

    private static Error Busy() => new(ChangedCode, "ساختار سازمانی همین حالا در حال تغییر است؛ چند لحظه بعد دوباره امتحان کنید.");

    private static Error ChangedMeanwhile() => new(ChangedCode, "این گره را کس دیگری همین حالا تغییر داده است؛ صفحه را تازه کنید و دوباره امتحان کنید.");

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex CodePattern();

    // ---------------------------------------------------------------- helpers

    private async Task LogAsync(Guid tenantId, string entityType, Guid entityId, string action, object details, CancellationToken cancellationToken) =>
        await repository.AddOrgChangeLogAsync(new OrgChangeLog(
            Guid.NewGuid(), tenantId, entityType, entityId, action, currentUser?.UserId, (clock ?? TimeProvider.System).GetUtcNow(),
            JsonSerializer.Serialize(details)), cancellationToken);

    /// <summary>The nodes beside this one: the same parent, or (at the top) the same company's top nodes.</summary>
    private static List<OrganizationUnit> SiblingsOf(NodeState state, OrganizationUnit unit)
    {
        var parent = state.Graph.ParentOf(unit.Id);
        var company = parent is null ? state.Graph.CompanyOfUnit(unit.Id) : null;
        return state.Units.Values
            .Where(other => state.Graph.ParentOf(other.Id) == parent && (parent is not null || state.Graph.CompanyOfUnit(other.Id) == company))
            .OrderBy(other => other.SortOrder).ThenBy(other => other.Name)
            .ToList();
    }

    private static int NextSortOrder(NodeState state, Guid? parentId, Guid? companyId, Guid? except = null)
    {
        var siblings = state.Units.Values.Where(other => other.Id != except
            && (parentId is not null ? other.ParentUnitId == parentId : state.Graph.ParentOf(other.Id) is null && state.Graph.CompanyOfUnit(other.Id) == companyId));
        return siblings.Select(other => other.SortOrder + 1).DefaultIfEmpty(0).Max();
    }

    private static int Depth(NodeState state, Guid unitId)
    {
        var depth = 0;
        var seen = new HashSet<Guid> { unitId };
        for (var parent = state.Graph.ParentOf(unitId); parent is { } p && seen.Add(p); parent = state.Graph.ParentOf(p))
        {
            depth++;
        }

        return depth;
    }

    private static IReadOnlyList<NodePathItemDto> PathOf(NodeState state, Guid unitId)
    {
        var path = new List<NodePathItemDto>();
        var seen = new HashSet<Guid> { unitId };
        for (var parent = state.Graph.ParentOf(unitId); parent is { } p && seen.Add(p); parent = state.Graph.ParentOf(p))
        {
            path.Add(new NodePathItemDto(p, state.Units[p].Name));
        }

        path.Reverse();
        return path;
    }

    /// <summary>A node of another company is hidden from someone kept to their own; a node in no company is not.</summary>
    private static bool Sees(NodeState state, IReadOnlySet<Guid>? mine, Guid unitId) =>
        mine is null || state.Graph.CompanyOfUnit(unitId) is not { } company || mine.Contains(company);

    private async Task<IReadOnlySet<Guid>?> MyCompaniesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        companyReach is not null && currentUser?.UserId is { } me
            ? await companyReach.GetCompaniesAsync(tenantId, me, cancellationToken)
            : null;

    /// <summary>Persian text compared loosely: Arabic ی/ك, half-spaces and case do not matter.</summary>
    internal static string Normalize(string? text) =>
        WhiteSpace().Replace((text ?? string.Empty).Replace('ي', 'ی').Replace('ك', 'ک').Replace('‌', ' '), " ").Trim().ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    private static OrgNodeDto ToDto(NodeState state, OrganizationUnit unit) => new(
        unit.Id,
        unit.Name,
        state.Graph.ParentOf(unit.Id),
        unit.NodeTypeId,
        unit.NodeTypeId is { } type && state.NodeTypes.TryGetValue(type, out var nodeType) ? nodeType.Name : null,
        state.Graph.CompanyOfUnit(unit.Id),
        unit.ManagerUserId,
        unit.IsActive,
        unit.SortOrder,
        state.Graph.ChildrenOf(unit.Id).Count,
        Convert.ToBase64String(unit.RowVersion ?? []),
        null,
        unit.NodeCode,
        unit.LatinName,
        unit.CorrespondenceTitle,
        unit.Phone,
        unit.Address);

    private static NodeTypeDto ToDto(NodeState state, NodeType type) => new(
        type.Id, type.Code, type.Name, type.Color, type.Icon, type.IsDomainBoundary, type.SortOrder, type.IsActive,
        state.Units.Values.Count(unit => unit.NodeTypeId == type.Id));

    private async Task<NodeState> LoadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var unitList = await units.ListAsync(tenantId, cancellationToken);
        var nodeTypes = await repository.ListNodeTypesAsync(tenantId, cancellationToken);
        var links = await repository.ListCompanyUnitsAsync(tenantId, cancellationToken);
        var seats = await repository.ListUnitPositionsAsync(tenantId, cancellationToken);
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        return new NodeState(
            unitList.ToDictionary(unit => unit.Id),
            nodeTypes.ToDictionary(type => type.Id),
            await repository.ListNodeTypeRulesAsync(tenantId, cancellationToken),
            links,
            (await repository.ListCompaniesAsync(tenantId, cancellationToken)).ToDictionary(company => company.Id),
            seats,
            holdings,
            await repository.ListAssignmentsAsync(tenantId, cancellationToken),
            OrgGraph.Build(unitList, nodeTypes, links, seats, holdings),
            (await repository.ListLinkTypesAsync(tenantId, cancellationToken)).ToDictionary(type => type.Id),
            await repository.ListAutoLinkRulesAsync(tenantId, cancellationToken),
            await repository.ListOrgLinksAsync(tenantId, cancellationToken));
    }

    /// <summary>One organization's nodes and what hangs on them, read in one go for a request.</summary>
    private sealed record NodeState(
        IReadOnlyDictionary<Guid, OrganizationUnit> Units,
        IReadOnlyDictionary<Guid, NodeType> NodeTypes,
        IReadOnlyList<NodeTypeRule> Rules,
        IReadOnlyList<CompanyUnit> Links,
        IReadOnlyDictionary<Guid, Company> Companies,
        IReadOnlyList<UnitPosition> Seats,
        IReadOnlyList<UserPosition> Holdings,
        IReadOnlyList<UserAssignment> Assignments,
        OrgGraph Graph,
        IReadOnlyDictionary<Guid, LinkType> LinkTypes,
        IReadOnlyList<AutoLinkRule> AutoLinkRules,
        IReadOnlyList<OrgLink> OrgLinks);
}
