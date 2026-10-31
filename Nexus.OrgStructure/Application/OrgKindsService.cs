using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// The simple chart (org / unit / position): the people of positions, the head of a unit, and the
/// whole tree in one read. The kinds are node types with fixed codes and the usual «what may sit
/// under what» rules, so creating, moving and the rules are the ordinary node service's. What this
/// adds is kept in step with the rest of the structure: a position's official person is the manager
/// of the node, a unit's head's official person is the unit's manager, and a person's first position
/// is their place (assignment) - so managers, tasks and chat see the same people as before.
/// </summary>
public interface IOrgKindsService
{
    Task<Result<KindTypesDto>> EnsureAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<KindChartDto>> GetChartAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result> AddPersonAsync(Guid tenantId, Guid nodeId, AddNodePersonRequest request, CancellationToken cancellationToken);
    Task<Result> RemovePersonAsync(Guid tenantId, Guid nodeId, Guid userId, CancellationToken cancellationToken);
    Task<Result> SetPrimaryPersonAsync(Guid tenantId, Guid nodeId, Guid userId, CancellationToken cancellationToken);
    Task<Result> SetHeadAsync(Guid tenantId, Guid unitId, SetNodeHeadRequest request, CancellationToken cancellationToken);
}

public sealed class OrgKindsService(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    IOrgNodeService nodes,
    IOrgStructureService structure,
    IUserDirectory users,
    TimeProvider? clock = null) : IOrgKindsService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<Result<KindTypesDto>> EnsureAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var typesResult = await nodes.ListNodeTypesAsync(tenantId, cancellationToken);
        if (!typesResult.IsSuccess)
        {
            return Result.Failure<KindTypesDto>(typesResult.Error);
        }

        var types = typesResult.Value!.ToList();
        var unit = await EnsureTypeAsync(tenantId, types, OrgNodeKinds.Unit, OrgNodeKinds.UnitTypeCode, OrgNodeKinds.UnitName, "#0e9384", "building-2", cancellationToken);
        if (!unit.IsSuccess)
        {
            return Result.Failure<KindTypesDto>(unit.Error);
        }

        var position = await EnsureTypeAsync(tenantId, types, OrgNodeKinds.Position, OrgNodeKinds.PositionTypeCode, OrgNodeKinds.PositionName, "#c9731a", "user", cancellationToken);
        if (!position.IsSuccess)
        {
            return Result.Failure<KindTypesDto>(position.Error);
        }

        var rulesResult = await nodes.GetRulesAsync(tenantId, cancellationToken);
        if (!rulesResult.IsSuccess)
        {
            return Result.Failure<KindTypesDto>(rulesResult.Error);
        }

        var ids = new Dictionary<string, Guid> { [OrgNodeKinds.Unit] = unit.Value!.Id, [OrgNodeKinds.Position] = position.Value!.Id };
        var existing = rulesResult.Value!.Rules.ToList();
        var missing = OrgNodeKinds.Pairs
            .Select(pair => new NodeTypeRuleDto(pair.Parent is null ? null : ids[pair.Parent], ids[pair.Child]))
            .Where(rule => !existing.Any(old => old.ParentNodeTypeId == rule.ParentNodeTypeId && old.ChildNodeTypeId == rule.ChildNodeTypeId))
            .ToList();
        if (missing.Count > 0)
        {
            var saved = await nodes.SetRulesAsync(tenantId, new SetNodeTypeRulesRequest([.. existing, .. missing]), cancellationToken);
            if (!saved.IsSuccess)
            {
                return Result.Failure<KindTypesDto>(saved.Error);
            }
        }

        return Result.Success(new KindTypesDto(unit.Value!.Id, position.Value!.Id));
    }

    private async Task<Result<NodeTypeDto>> EnsureTypeAsync(
        Guid tenantId, List<NodeTypeDto> types, string kind, string code, string name, string color, string icon, CancellationToken cancellationToken)
    {
        var found = types.FirstOrDefault(type => type.IsActive && OrgNodeKinds.KindOfTypeCode(type.Code) == kind);
        if (found is not null)
        {
            return Result.Success(found);
        }

        var made = await nodes.CreateNodeTypeAsync(tenantId, new CreateNodeTypeRequest(name, code, color, icon, false), cancellationToken);
        if (made.IsSuccess)
        {
            types.Add(made.Value!);
        }

        return made;
    }

    public async Task<Result<KindChartDto>> GetChartAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var list = await nodes.ListNodesAsync(tenantId, null, cancellationToken);
        if (!list.IsSuccess)
        {
            return Result.Failure<KindChartDto>(list.Error);
        }

        var typesResult = await nodes.ListNodeTypesAsync(tenantId, cancellationToken);
        if (!typesResult.IsSuccess)
        {
            return Result.Failure<KindChartDto>(typesResult.Error);
        }

        var kindOfType = typesResult.Value!.ToDictionary(type => type.Id, type => OrgNodeKinds.KindOfTypeCode(type.Code));
        var visible = list.Value!.ToDictionary(node => node.Id);
        var persons = (await repository.ListNodePersonsAsync(tenantId, cancellationToken)).Where(p => visible.ContainsKey(p.UnitId)).ToList();
        var heads = await repository.ListNodeHeadsAsync(tenantId, cancellationToken);
        var contacts = (await users.GetUsersAsync(persons.Select(p => p.UserId).Distinct().ToList(), cancellationToken)).ToDictionary(c => c.Id);

        string KindOf(OrgNodeDto node) => (node.NodeTypeId is { } typeId && kindOfType.TryGetValue(typeId, out var kind) ? kind : null) ?? OrgNodeKinds.Unit;

        var nodesOut = list.Value!.Select(node =>
        {
            var kind = KindOf(node);
            var head = kind == OrgNodeKinds.Unit ? heads.FirstOrDefault(h => h.UnitId == node.Id) : null;
            var validHead = head is not null && visible.TryGetValue(head.HeadUnitId, out var headNode) && headNode.ParentNodeId == node.Id ? head.HeadUnitId : (Guid?)null;
            var people = persons.Where(p => p.UnitId == node.Id)
                .Select(p => new KindPersonDto(p.UserId, contacts.TryGetValue(p.UserId, out var contact) ? contact.DisplayName : "—", p.IsPrimary))
                .ToList();
            return new KindNodeDto(node.Id, node.Name, kind, node.ParentNodeId, node.CompanyId, node.IsActive, node.SortOrder, node.RowVersion, validHead, people);
        }).ToList();
        return Result.Success(new KindChartDto(nodesOut));
    }

    // ---------------------------------------------------------------- people

    public async Task<Result> AddPersonAsync(Guid tenantId, Guid nodeId, AddNodePersonRequest request, CancellationToken cancellationToken)
    {
        var node = await PositionAsync(tenantId, nodeId, cancellationToken);
        if (!node.IsSuccess)
        {
            return Result.Failure(node.Error);
        }

        if ((await users.GetUsersAsync([request.UserId], cancellationToken)).Count == 0)
        {
            return Result.Failure(Error.Validation("کاربر پیدا نشد."));
        }

        var all = await repository.ListNodePersonsAsync(tenantId, cancellationToken);
        if (all.Any(p => p.UnitId == nodeId && p.UserId == request.UserId))
        {
            return Result.Failure(Error.Validation("این شخص پیش‌تر در این سمت ثبت شده است."));
        }

        var first = !all.Any(p => p.UnitId == nodeId);
        await repository.AddNodePersonAsync(new NodePerson(Guid.NewGuid(), tenantId, nodeId, request.UserId, first, _clock.GetUtcNow()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await SyncAsync(tenantId, node.Value!, [request.UserId], cancellationToken);
    }

    public async Task<Result> RemovePersonAsync(Guid tenantId, Guid nodeId, Guid userId, CancellationToken cancellationToken)
    {
        var node = await PositionAsync(tenantId, nodeId, cancellationToken);
        if (!node.IsSuccess)
        {
            return Result.Failure(node.Error);
        }

        var all = await repository.ListNodePersonsAsync(tenantId, cancellationToken);
        var person = all.FirstOrDefault(p => p.UnitId == nodeId && p.UserId == userId);
        if (person is null)
        {
            return Result.Failure(Error.NotFound("این شخص در این سمت نیست."));
        }

        repository.RemoveNodePerson(person);
        if (person.IsPrimary && all.FirstOrDefault(p => p.UnitId == nodeId && p.UserId != userId) is { } next)
        {
            next.SetPrimary(true);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await SyncAsync(tenantId, node.Value!, [userId], cancellationToken);
    }

    public async Task<Result> SetPrimaryPersonAsync(Guid tenantId, Guid nodeId, Guid userId, CancellationToken cancellationToken)
    {
        var node = await PositionAsync(tenantId, nodeId, cancellationToken);
        if (!node.IsSuccess)
        {
            return Result.Failure(node.Error);
        }

        var here = (await repository.ListNodePersonsAsync(tenantId, cancellationToken)).Where(p => p.UnitId == nodeId).ToList();
        if (here.All(p => p.UserId != userId))
        {
            return Result.Failure(Error.NotFound("این شخص در این سمت نیست."));
        }

        foreach (var person in here)
        {
            person.SetPrimary(person.UserId == userId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await SyncAsync(tenantId, node.Value!, [], cancellationToken);
    }

    // ---------------------------------------------------------------- head

    public async Task<Result> SetHeadAsync(Guid tenantId, Guid unitId, SetNodeHeadRequest request, CancellationToken cancellationToken)
    {
        var unit = await nodes.GetNodeAsync(tenantId, unitId, cancellationToken);
        if (!unit.IsSuccess)
        {
            return Result.Failure(unit.Error);
        }

        var kinds = await KindsAsync(tenantId, cancellationToken);
        if (kinds is null || KindOf(unit.Value!, kinds) != OrgNodeKinds.Unit)
        {
            return Result.Failure(Error.Validation("فقط واحد سازمانی رئیس دارد."));
        }

        if (request.HeadNodeId is { } headId)
        {
            var head = await nodes.GetNodeAsync(tenantId, headId, cancellationToken);
            if (!head.IsSuccess || head.Value!.ParentNodeId != unitId || KindOf(head.Value!, kinds) != OrgNodeKinds.Position)
            {
                return Result.Failure(Error.Validation("رئیس باید یکی از سمت‌های مستقیم همین واحد باشد."));
            }
        }

        var existing = (await repository.ListNodeHeadsAsync(tenantId, cancellationToken)).FirstOrDefault(h => h.UnitId == unitId);
        if (request.HeadNodeId is { } newHead)
        {
            if (existing is null)
            {
                await repository.AddNodeHeadAsync(new NodeHead(Guid.NewGuid(), tenantId, unitId, newHead), cancellationToken);
            }
            else
            {
                existing.SetHead(newHead);
            }
        }
        else if (existing is not null)
        {
            repository.RemoveNodeHead(existing);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await SyncManagerAsync(tenantId, unit.Value!, cancellationToken);
    }

    // ---------------------------------------------------------------- keeping the rest of the structure in step

    private static string? KindOf(OrgNodeDto node, IReadOnlyDictionary<Guid, string?> kinds) =>
        (node.NodeTypeId is { } typeId && kinds.TryGetValue(typeId, out var kind) ? kind : null) ?? OrgNodeKinds.Unit;

    private async Task<Dictionary<Guid, string?>?> KindsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var types = await nodes.ListNodeTypesAsync(tenantId, cancellationToken);
        return types.IsSuccess ? types.Value!.ToDictionary(type => type.Id, type => OrgNodeKinds.KindOfTypeCode(type.Code)) : null;
    }

    private async Task<Result<OrgNodeDto>> PositionAsync(Guid tenantId, Guid nodeId, CancellationToken cancellationToken)
    {
        var node = await nodes.GetNodeAsync(tenantId, nodeId, cancellationToken);
        if (!node.IsSuccess)
        {
            return node;
        }

        var kinds = await KindsAsync(tenantId, cancellationToken);
        return kinds is not null && KindOf(node.Value!, kinds) == OrgNodeKinds.Position
            ? node
            : Result.Failure<OrgNodeDto>(Error.Validation("فقط سمت / پست سازمانی شخص می‌گیرد."));
    }

    /// <summary>After people of a position changed: its manager, its unit's manager (when it is the head) and each person's place.</summary>
    private async Task<Result> SyncAsync(Guid tenantId, OrgNodeDto position, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var self = await SyncManagerAsync(tenantId, position, cancellationToken);
        if (!self.IsSuccess)
        {
            return self;
        }

        if (position.ParentNodeId is { } parentId)
        {
            var parent = await nodes.GetNodeAsync(tenantId, parentId, cancellationToken);
            if (parent.IsSuccess)
            {
                var up = await SyncManagerAsync(tenantId, parent.Value!, cancellationToken);
                if (!up.IsSuccess)
                {
                    return up;
                }
            }
        }

        foreach (var userId in userIds)
        {
            var placed = await SyncPlaceAsync(tenantId, userId, cancellationToken);
            if (!placed.IsSuccess)
            {
                return placed;
            }
        }

        return Result.Success();
    }

    /// <summary>A position's manager is its official person; a unit's is its head's official person (or none).</summary>
    private async Task<Result> SyncManagerAsync(Guid tenantId, OrgNodeDto node, CancellationToken cancellationToken)
    {
        var kinds = await KindsAsync(tenantId, cancellationToken);
        if (kinds is null)
        {
            return Result.Success();
        }

        var kind = KindOf(node, kinds);
        var persons = await repository.ListNodePersonsAsync(tenantId, cancellationToken);
        Guid? manager = null;
        if (kind == OrgNodeKinds.Position)
        {
            manager = Official(persons, node.Id);
        }
        else if (kind == OrgNodeKinds.Unit)
        {
            var head = (await repository.ListNodeHeadsAsync(tenantId, cancellationToken)).FirstOrDefault(h => h.UnitId == node.Id);
            manager = head is null ? null : Official(persons, head.HeadUnitId);
        }

        var current = await nodes.GetNodeAsync(tenantId, node.Id, cancellationToken);
        if (!current.IsSuccess || current.Value!.ManagerUserId == manager)
        {
            return Result.Success();
        }

        var saved = await nodes.SetManagerAsync(tenantId, node.Id, new SetNodeManagerRequest(manager, current.Value!.RowVersion), cancellationToken);
        return saved.IsSuccess ? Result.Success() : Result.Failure(saved.Error);
    }

    private static Guid? Official(IReadOnlyList<NodePerson> persons, Guid nodeId)
    {
        var here = persons.Where(p => p.UnitId == nodeId).ToList();
        return (here.FirstOrDefault(p => p.IsPrimary) ?? here.FirstOrDefault())?.UserId;
    }

    /// <summary>A person's place (assignment) is the first position they were put in; nobody in any position: the place is cleared when it was one of ours.</summary>
    private async Task<Result> SyncPlaceAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var mine = (await repository.ListNodePersonsAsync(tenantId, cancellationToken)).Where(p => p.UserId == userId).OrderBy(p => p.AddedAtUtc).ToList();
        var assignment = await repository.GetAssignmentAsync(tenantId, userId, cancellationToken);
        if (mine.Count == 0)
        {
            return assignment?.UnitId is not null ? await structure.RemoveAssignmentAsync(tenantId, userId, cancellationToken) : Result.Success();
        }

        var node = await nodes.GetNodeAsync(tenantId, mine[0].UnitId, cancellationToken);
        if (!node.IsSuccess || node.Value!.CompanyId is not { } companyId)
        {
            return Result.Success();
        }

        if (assignment is not null && assignment.UnitId == mine[0].UnitId)
        {
            return Result.Success();
        }

        var set = await structure.SetAssignmentAsync(tenantId, userId, new SetUserAssignmentRequest(companyId, mine[0].UnitId, null, null), cancellationToken);
        return set.IsSuccess ? Result.Success() : Result.Failure(set.Error);
    }
}
