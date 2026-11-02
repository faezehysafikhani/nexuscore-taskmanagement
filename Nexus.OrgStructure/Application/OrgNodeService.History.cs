using System.Text.Json;
using Nexus.Organization.Domain;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Phase 6.4: when a node exists (ValidFrom / ValidTo), merging a node into another, the change log
/// and undoing a node's last change. Changes take effect at once; the dates only say from when and
/// until when a node exists, for the chart on a given day.
/// </summary>
public interface IOrgNodeHistoryService
{
    Task<Result<OrgNodeDto>> SetValidityAsync(Guid tenantId, Guid id, NodeValidityRequest request, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> MergeAsync(Guid tenantId, Guid sourceId, MergeNodeRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<NodeChangeDto>>> ListChangesAsync(Guid tenantId, Guid id, int take, CancellationToken cancellationToken);
    Task<Result<OrgNodeDto>> UndoLastChangeAsync(Guid tenantId, Guid id, NodeVersionRequest request, CancellationToken cancellationToken);
}

public sealed partial class OrgNodeService : IOrgNodeHistoryService
{
    public Task<Result<OrgNodeDto>> SetValidityAsync(Guid tenantId, Guid id, NodeValidityRequest request, CancellationToken cancellationToken) =>
        ChangeNodeAsync(tenantId, id, request.RowVersion, "validity", (state, unit) =>
        {
            if (request.ValidFrom is { } from && request.ValidTo is { } to && to <= from)
            {
                return Task.FromResult<(Error?, object?)>((Error.Validation("تاریخ پایان باید بعد از تاریخ شروع باشد."), null));
            }

            var before = new { validFrom = unit.ValidFrom, validTo = unit.ValidTo };
            unit.SetValidity(request.ValidFrom, request.ValidTo);
            return Task.FromResult<(Error?, object?)>((null, new { before, after = new { validFrom = request.ValidFrom, validTo = request.ValidTo } }));
        }, cancellationToken);

    // ---------------------------------------------------------------- merge

    /// <summary>
    /// The source goes into the target (same company): its child nodes, its posts (with their holders,
    /// so open tasks stay on the same posts) and its people move to the target; the source becomes
    /// inactive and ends on the merge date. The target keeps its own manager post.
    /// </summary>
    public Task<Result<OrgNodeDto>> MergeAsync(Guid tenantId, Guid sourceId, MergeNodeRequest request, CancellationToken cancellationToken) =>
        ChangeNodeAsync(tenantId, sourceId, request.RowVersion, "merge", async (state, source) =>
        {
            var mine = await MyCompaniesAsync(tenantId, cancellationToken);
            if (MergeProblem(state, mine, source, request.TargetNodeId) is { } problem)
            {
                return (problem, null);
            }

            var target = state.Units[request.TargetNodeId];
            var children = state.Units.Values.Where(unit => unit.ParentUnitId == source.Id).OrderBy(unit => unit.SortOrder).ToList();
            var order = state.Units.Values.Where(unit => unit.ParentUnitId == target.Id).Select(unit => unit.SortOrder).DefaultIfEmpty(-1).Max() + 1;
            var childOrders = children.ToDictionary(child => child.Id, child => child.SortOrder);
            foreach (var child in children)
            {
                child.SetParent(target.Id);
                child.SetSortOrder(order++);
            }

            var seats = state.Seats.Where(seat => seat.UnitId == source.Id).ToList();
            var targetManager = state.Seats.Any(seat => seat.UnitId == target.Id && seat.IsManagerPost);
            var unmarked = new List<Guid>();
            foreach (var seat in seats)
            {
                seat.MoveToUnit(target.Id);
                if (seat.IsManagerPost && targetManager)
                {
                    seat.SetManagerPost(false);
                    unmarked.Add(seat.Id);
                }
            }

            var people = state.Assignments.Where(a => a.UnitId == source.Id).ToList();
            foreach (var assignment in people)
            {
                assignment.Set(assignment.CompanyId, target.Id, assignment.PositionId, assignment.ManagerUserId);
            }

            var before = new { isActive = source.IsActive, validTo = source.ValidTo };
            var ends = request.EffectiveDate ?? (clock ?? TimeProvider.System).GetUtcNow();
            source.Update(source.Name, source.ManagerUserId, isActive: false);
            source.SetValidity(source.ValidFrom, ends);
            return (null, new
            {
                target = target.Id,
                children = children.Select(child => new { id = child.Id, sortOrder = childOrders[child.Id] }),
                posts = seats.Select(seat => seat.Id),
                unmarked,
                people = people.Select(a => a.UserId),
                before,
                after = new { isActive = false, validTo = ends },
            });
        }, cancellationToken);

    /// <summary>Why the source cannot go into the target, or null (also used by the impact preview).</summary>
    private static Error? MergeProblem(NodeState state, IReadOnlySet<Guid>? mine, OrganizationUnit source, Guid targetId)
    {
        if (!state.Units.TryGetValue(targetId, out var target) || !Sees(state, mine, targetId))
        {
            return Error.NotFound("گره‌ی مقصد پیدا نشد.");
        }

        if (target.Id == source.Id || state.Graph.SubtreeOf(source.Id).Contains(target.Id))
        {
            return Error.Validation("گره را نمی‌شود در خودش یا در زیرمجموعه‌ی خودش ادغام کرد.");
        }

        if (!source.IsActive || !target.IsActive)
        {
            return Error.Validation("هر دو گره باید فعال باشند.");
        }

        if (state.Graph.CompanyOfUnit(source.Id) != state.Graph.CompanyOfUnit(target.Id))
        {
            return Error.Validation("ادغام فقط بین گره‌های یک شرکت است.");
        }

        // Each child must be allowed under the target, by the rules.
        foreach (var child in state.Units.Values.Where(unit => unit.ParentUnitId == source.Id))
        {
            if (RulesProblem(state, child, target.Id, child.NodeTypeId, isNew: false) is { } ruleProblem)
            {
                return Error.Validation($"«{child.Name}»: {ruleProblem.Message}");
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- the log and undo

    public async Task<Result<IReadOnlyList<NodeChangeDto>>> ListChangesAsync(Guid tenantId, Guid id, int take, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        if (!state.Units.ContainsKey(id) || !Sees(state, await MyCompaniesAsync(tenantId, cancellationToken), id))
        {
            return Result.Failure<IReadOnlyList<NodeChangeDto>>(NodeNotFound());
        }

        var log = await repository.ListOrgChangeLogsAsync(tenantId, id, Math.Clamp(take, 1, 200), cancellationToken);
        return Result.Success<IReadOnlyList<NodeChangeDto>>(log.Select(entry => new NodeChangeDto(entry.Id, entry.Action, entry.UserId, entry.AtUtc, entry.Details, Undoable.Contains(entry.Action))).ToList());
    }

    private static readonly string[] Undoable = ["update", "manager", "details", "validity", "move", "deactivate", "activate", "merge"];

    /// <summary>
    /// Puts back what the node's last change changed. The change itself is checked again (rules,
    /// loops, versions) and logged; a merge is undone only while nothing it moved has moved since.
    /// </summary>
    public async Task<Result<OrgNodeDto>> UndoLastChangeAsync(Guid tenantId, Guid id, NodeVersionRequest request, CancellationToken cancellationToken)
    {
        var last = (await repository.ListOrgChangeLogsAsync(tenantId, id, 1, cancellationToken)).FirstOrDefault();
        if (last is null || last.EntityType != "Node")
        {
            return Result.Failure<OrgNodeDto>(Error.Validation("این گره تغییری برای برگشت ندارد."));
        }

        if (!Undoable.Contains(last.Action))
        {
            return Result.Failure<OrgNodeDto>(Error.Validation(last.Action switch
            {
                "unmerge" => "آخرین تغییر خودش برگشت ادغام بوده است.",
                "create" => "ساختن گره برنمی‌گردد؛ برای برداشتنش آن را غیرفعال کنید.",
                _ => $"تغییر «{last.Action}» برنمی‌گردد.",
            }));
        }

        using var details = JsonDocument.Parse(last.Details ?? "{}");
        var root = details.RootElement;
        JsonElement Before() => root.GetProperty("before");
        static Guid? IdOf(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();
        static string? TextOf(JsonElement value, string name) =>
            value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static DateTimeOffset? DateOf(JsonElement value, string name) =>
            value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetDateTimeOffset() : null;

        switch (last.Action)
        {
            case "update":
                return await UpdateNodeAsync(tenantId, id, new UpdateNodeRequest(TextOf(Before(), "name") ?? string.Empty, IdOf(Before().GetProperty("type")), request.RowVersion), cancellationToken);
            case "manager":
                return await SetManagerAsync(tenantId, id, new SetNodeManagerRequest(IdOf(Before()), request.RowVersion), cancellationToken);
            case "details":
                var b = Before();
                return await SetDetailsAsync(tenantId, id, new SetNodeDetailsRequest(TextOf(b, "code"), TextOf(b, "LatinName"), TextOf(b, "CorrespondenceTitle"), TextOf(b, "Phone"), TextOf(b, "Address"), request.RowVersion), cancellationToken);
            case "validity":
                return await SetValidityAsync(tenantId, id, new NodeValidityRequest(DateOf(Before(), "validFrom"), DateOf(Before(), "validTo"), request.RowVersion), cancellationToken);
            case "move":
                return await MoveNodeAsync(tenantId, id, new MoveNodeRequest(IdOf(Before().GetProperty("parent")), IdOf(Before().GetProperty("company")), request.RowVersion), cancellationToken);
            case "deactivate":
                return await ActivateNodeAsync(tenantId, id, request, cancellationToken);
            case "activate":
                return await DeactivateNodeAsync(tenantId, id, request, cancellationToken);
            default:
                return await UnmergeAsync(tenantId, id, root, request, cancellationToken);
        }
    }

    private Task<Result<OrgNodeDto>> UnmergeAsync(Guid tenantId, Guid sourceId, JsonElement merge, NodeVersionRequest request, CancellationToken cancellationToken) =>
        ChangeNodeAsync(tenantId, sourceId, request.RowVersion, "unmerge", (state, source) =>
        {
            var targetId = merge.GetProperty("target").GetGuid();
            var children = merge.GetProperty("children").EnumerateArray().Select(c => (Id: c.GetProperty("id").GetGuid(), Order: c.GetProperty("sortOrder").GetInt32())).ToList();
            var posts = merge.GetProperty("posts").EnumerateArray().Select(p => p.GetGuid()).ToList();
            var unmarked = merge.GetProperty("unmarked").EnumerateArray().Select(p => p.GetGuid()).ToHashSet();
            var people = merge.GetProperty("people").EnumerateArray().Select(p => p.GetGuid()).ToList();

            // Only while everything is still where the merge put it.
            var moved = children.Where(c => !state.Units.TryGetValue(c.Id, out var child) || child.ParentUnitId != targetId).Select(c => c.Id)
                .Concat(posts.Where(p => state.Seats.FirstOrDefault(seat => seat.Id == p)?.UnitId != targetId))
                .Concat(people.Where(u => state.Assignments.FirstOrDefault(a => a.UserId == u)?.UnitId != targetId))
                .Count();
            if (source.IsActive || moved > 0)
            {
                return Task.FromResult<(Error?, object?)>((Error.Conflict(
                    $"از زمان ادغام {FaDigits.Of(moved)} گره، پست یا فرد جابه‌جا شده یا گره دوباره فعال شده است؛ ادغام برنمی‌گردد."), null));
            }

            foreach (var (childId, order) in children)
            {
                state.Units[childId].SetParent(source.Id);
                state.Units[childId].SetSortOrder(order);
            }

            foreach (var seat in state.Seats.Where(seat => posts.Contains(seat.Id)))
            {
                seat.MoveToUnit(source.Id);
                if (unmarked.Contains(seat.Id))
                {
                    seat.SetManagerPost(true);
                }
            }

            foreach (var assignment in state.Assignments.Where(a => people.Contains(a.UserId)))
            {
                assignment.Set(assignment.CompanyId, source.Id, assignment.PositionId, assignment.ManagerUserId);
            }

            var before = merge.GetProperty("before");
            source.Update(source.Name, source.ManagerUserId, before.GetProperty("isActive").GetBoolean());
            source.SetValidity(source.ValidFrom, before.GetProperty("validTo").ValueKind == JsonValueKind.Null ? null : before.GetProperty("validTo").GetDateTimeOffset());
            return Task.FromResult<(Error?, object?)>((null, new { target = targetId, children = children.Count, posts = posts.Count, people = people.Count }));
        }, cancellationToken);
}
