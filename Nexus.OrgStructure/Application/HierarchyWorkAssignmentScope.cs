using Microsoft.Extensions.Options;
using Nexus.Organization.Application;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Application;

public sealed class OrgStructureOptions
{
    public const string SectionName = "OrgStructure";

    /// <summary>
    /// Whether people make responsible for tasks only those at their own level or below
    /// (<see cref="HierarchyWorkAssignmentScope"/>). Off unless the product turns it on.
    /// </summary>
    public bool LimitTaskAssignment { get; set; }

    /// <summary>
    /// Whether tasks are given to posts instead of people (<see cref="TaskPostDirectory"/>): a task
    /// moves with its posts to their new holders. Off unless the product turns it on.
    /// </summary>
    public bool AssignTasksToPosts { get; set; }

    /// <summary>
    /// Whether everyone is kept to their own company (<see cref="CompanyReachScope"/>): they see,
    /// message, add to teams and give tasks only to its people, and see only its structure.
    /// Holders of OrgStructurePermissions.AllCompanies are not limited. Off unless the product turns it on.
    /// </summary>
    public bool LimitToOwnCompany { get; set; }
}

/// <summary>
/// With <see cref="OrgStructureOptions.LimitTaskAssignment"/> on, a user makes responsible for a
/// task only: themselves, the people below them (their subordinates, at any depth), and the people
/// of their own company at their own level - as many managers above them as they have - except, in their own deputy,
/// someone in a position above theirs (the deputy's positions are in rank order). Never anyone
/// above them.
///
/// Not limited: a user with no place in the structure, and holders of
/// <see cref="OrgStructurePermissions.AssignTasksToAnyLevel"/>. Anyone with no place yet may be
/// made responsible by anyone. Only people newly made responsible are checked.
/// </summary>
public sealed class HierarchyWorkAssignmentScope(
    IOrgStructureRepository repository,
    IOrganizationUnitRepository units,
    ICurrentUserContext currentUser,
    IOptions<OrgStructureOptions> options) : IWorkAssignmentScope
{
    public async Task<WorkAssignmentLimit?> GetLimitAsync(Guid tenantId, Guid assignerUserId, CancellationToken cancellationToken)
    {
        if (!options.Value.LimitTaskAssignment
            || (currentUser.UserId == assignerUserId && currentUser.HasPermission(OrgStructurePermissions.AssignTasksToAnyLevel)))
        {
            return null;
        }

        var companies = await repository.ListCompaniesAsync(tenantId, cancellationToken);
        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        var heads = companies.Where(c => c.ManagerUserId is not null).Select(c => c.ManagerUserId!.Value).ToHashSet();
        var placed = assignments.Select(a => a.UserId).Concat(heads).ToHashSet();
        if (!placed.Contains(assignerUserId))
        {
            return null;
        }

        var seatList = await repository.ListUnitPositionsAsync(tenantId, cancellationToken);
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        var postSuperiors = PostSuperiors.Of(
            assignments.Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId)), seatList, holdings);
        var resolver = new HierarchyResolver(
            (await units.ListAsync(tenantId, cancellationToken)).Select(unit => new UnitNode(unit.Id, null, unit.ManagerUserId)),
            (await repository.ListCompanyUnitsAsync(tenantId, cancellationToken)).ToDictionary(link => link.UnitId, link => link.CompanyId),
            companies.ToDictionary(company => company.Id, company => company.ManagerUserId),
            assignments.Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId, postSuperiors.TryGetValue(a.UserId, out var superior) ? superior : null)));

        // A person's rank in their own deputy: the highest of the deputy's positions they hold.
        var seats = seatList.ToDictionary(seat => seat.Id);
        var unitOf = assignments.ToDictionary(a => a.UserId, a => a.UnitId);
        int? RankOf(Guid userId) => unitOf.GetValueOrDefault(userId) is { } unitId
            ? holdings.Where(h => h.UserId == userId && seats.TryGetValue(h.UnitPositionId, out var seat) && seat.UnitId == unitId)
                .Select(h => (int?)seats[h.UnitPositionId].SortOrder).Min()
            : null;

        var level = resolver.ManagementChainOf(assignerUserId).Count;
        var below = resolver.AllSubordinatesOf(assignerUserId).Select(s => s.UserId).ToHashSet();
        var assignerUnit = unitOf.GetValueOrDefault(assignerUserId);
        var assignerRank = RankOf(assignerUserId);

        // The companies a person is in: where they are placed, and those they head.
        var companiesOf = assignments.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.Select(a => a.CompanyId).ToHashSet());
        foreach (var company in companies.Where(c => c.ManagerUserId is not null))
        {
            if (!companiesOf.TryGetValue(company.ManagerUserId!.Value, out var set))
            {
                companiesOf[company.ManagerUserId.Value] = set = [];
            }

            set.Add(company.Id);
        }

        var assignerCompanies = companiesOf.GetValueOrDefault(assignerUserId) ?? [];

        bool Allowed(Guid userId)
        {
            if (userId == assignerUserId || below.Contains(userId))
            {
                return true;
            }

            // At one's own level only within one's own company: other companies' people are not peers.
            if (resolver.ManagementChainOf(userId).Count != level
                || !(companiesOf.GetValueOrDefault(userId)?.Overlaps(assignerCompanies) ?? false))
            {
                return false;
            }

            // Same level - but not someone in a higher position of the same deputy.
            return assignerUnit is null || unitOf.GetValueOrDefault(userId) != assignerUnit
                || RankOf(userId) is not { } rank || assignerRank is not { } own || rank >= own;
        }

        return new WorkAssignmentLimit(placed.Where(userId => !Allowed(userId)).ToHashSet());
    }
}
