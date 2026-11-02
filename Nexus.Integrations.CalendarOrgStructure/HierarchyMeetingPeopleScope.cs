using Microsoft.EntityFrameworkCore;
using Nexus.OrgStructure.Application;
using Nexus.Organization.Application;
using Nexus.Calendar.Application.Meetings;
using Nexus.Calendar.Permissions;
using NexusCore.Infrastructure.Persistence;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Integrations.CalendarOrgStructure;

/// <summary>Meeting-only rules. Task/company administrator permissions never bypass these boundaries.</summary>
public sealed class HierarchyMeetingPeopleScope(IOrgStructureRepository repository, IOrgGraphProvider graphs, IOrganizationUnitRepository units,
    NexusCoreDbContext identity, ICurrentUserContext current) : IMeetingPeopleScope
{
    public async Task<MeetingPeopleOptions> GetAsync(CancellationToken ct)
    {
        var higher = current.HasPermission(MeetingPermissions.InviteHigher);
        var allPeople = current.HasPermission(MeetingPermissions.AccessAllPeople);
        if (current.TenantId is not { } tenant || current.UserId is not { } actor)
            return new(false, false, [], "ابتدا وارد سامانه شوید.");

        var companies = (await repository.ListCompaniesAsync(tenant, ct)).Where(c => c.IsActive).ToList();
        var companyIds = companies.Select(c => c.Id).ToHashSet();
        var graph = await graphs.GetAsync(tenant, ct);
        var unitRows = (await units.ListAsync(tenant, ct)).ToDictionary(u => u.Id);
        bool ActiveUnit(Guid unit)
        {
            var seen = new HashSet<Guid>();
            Guid? at = unit;
            while (at is { } id)
            {
                if (!seen.Add(id) || !unitRows.TryGetValue(id, out var row) || !row.IsActive || !OrgGraph.IsValidAt(row, DateTimeOffset.UtcNow)) return false;
                at = graph.ParentOf(id);
            }
            return true;
        }
        var positions = (await repository.ListPositionsAsync(tenant, ct)).Where(p => p.IsActive).ToDictionary(p => p.Id);
        var seats = await repository.ListUnitPositionsAsync(tenant, ct);
        // The repository excludes appointments that have not started or have already ended.
        var holdings = await repository.ListUserPositionsAsync(tenant, ct);
        var activeUsers = await identity.Users.AsNoTracking().Where(u => u.TenantId == tenant && u.IsActive)
            .Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, ct);
        var assignments = (await repository.ListAssignmentsAsync(tenant, ct))
            .Where(a => activeUsers.ContainsKey(a.UserId) && companyIds.Contains(a.CompanyId)
                && (a.UnitId is null || ActiveUnit(a.UnitId.Value) && graph.CompanyOfUnit(a.UnitId.Value) == a.CompanyId)
                && holdings.Any(h => h.UserId == a.UserId && seats.Any(s => s.Id == h.UnitPositionId
                    && s.CompanyId == a.CompanyId && s.UnitId == a.UnitId && positions.ContainsKey(s.PositionId))))
            .ToList();
        if (allPeople)
        {
            var activePosition = assignments.ToDictionary(a => a.UserId, a =>
            {
                var post = holdings.Where(h => h.UserId == a.UserId)
                    .Select(h => seats.FirstOrDefault(s => s.Id == h.UnitPositionId && positions.ContainsKey(s.PositionId)))
                    .FirstOrDefault(s => s is not null);
                return post is null ? "بدون سمت فعال" : positions[post.PositionId].Title;
            });
            return new(current.HasPermission(MeetingPermissions.Create), higher,
                activeUsers.Values.Select(user => new MeetingPerson(user.Id, user.DisplayName,
                    activePosition.GetValueOrDefault(user.Id) ?? "بدون سمت فعال", user.Id == actor ? "self" : "all"))
                    .OrderBy(person => person.Name).ToList());
        }
        var mine = assignments.FirstOrDefault(a => a.UserId == actor);
        if (mine is null)
            return new(false, higher, [], "برای ایجاد جلسه و انتخاب افراد، باید سمت سازمانی فعال یا دسترسی «همه افراد جلسات» داشته باشید.");

        var superiors = PostSuperiors.Of(assignments.Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId)), seats, holdings);
        var resolver = new HierarchyResolver(graph, companies.ToDictionary(c => c.Id, c => c.ManagerUserId),
            assignments.Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId, superiors.TryGetValue(a.UserId, out var superior) ? superior : null)));
        var levels = new WorkAssignmentLevels(assignments, companies, seats, holdings, positions, resolver);
        var above = resolver.ManagementChainOf(actor).ToHashSet();
        var below = resolver.AllSubordinatesOf(actor).Select(x => x.UserId).ToHashSet();
        var subtree = mine.UnitId is { } unit ? graph.SubtreeOf(unit).ToHashSet() : null;
        bool Allowed(Nexus.OrgStructure.Domain.UserAssignment target)
        {
            if (target.CompanyId != mine.CompanyId) return false;
            if (target.UserId == actor) return true;
            var sameUnit = target.UnitId == mine.UnitId;
            var descendantUnit = target.UnitId is { } child && (subtree?.Contains(child) ?? true);
            var ancestorUnit = target.UnitId is null || (mine.UnitId is { } own && target.UnitId is { } parent && graph.SubtreeOf(parent).Contains(own));
            if (higher && above.Contains(target.UserId) && (sameUnit || ancestorUnit)) return true;
            if (sameUnit) return levels.IsAllowed(actor, target.UserId);
            if (descendantUnit && below.Contains(target.UserId)) return true;
            // Parallel units: peers only, within the same deputy. Neither their managers nor their staff.
            var sameDeputy = mine.UnitId is { } ownUnit && target.UnitId is { } otherUnit
                && graph.DomainOf(ownUnit) == graph.DomainOf(otherUnit);
            return !descendantUnit && !ancestorUnit && sameDeputy && levels.LevelOf(actor) == levels.LevelOf(target.UserId);
        }
        var people = assignments.Where(Allowed).Select(a =>
        {
            var post = holdings.Where(h => h.UserId == a.UserId).Select(h => seats.FirstOrDefault(s => s.Id == h.UnitPositionId
                && s.CompanyId == a.CompanyId && s.UnitId == a.UnitId && positions.ContainsKey(s.PositionId))).First(s => s is not null)!;
            return new MeetingPerson(a.UserId, activeUsers[a.UserId].DisplayName, positions[post.PositionId].Title,
                a.UserId == actor ? "self" : above.Contains(a.UserId) ? "superior" : below.Contains(a.UserId) ? "subordinate" : "peer");
        }).OrderBy(p => p.Name).ToList();
        return new(current.HasPermission(MeetingPermissions.Create), higher, people);
    }
}
