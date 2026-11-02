using Nexus.Organization.Application;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// With the organization structure, the builder of a work team adds the people of their own
/// deputy only - unless they hold <see cref="OrgStructurePermissions.TeamMembersFromAnyUnit"/>.
/// A builder in no deputy (directly under a company, or with no place yet) is not limited by it.
/// Where everyone is kept to their own company (<see cref="CompanyReachScope"/>), only that
/// company's people may be added either way.
/// </summary>
public sealed class DeputyWorkTeamMemberScope(
    IOrgStructureRepository repository,
    IOrganizationUnitRepository units,
    ICurrentUserContext currentUser,
    CompanyReachScope? companyReach = null,
    IOrgGraphProvider? graphs = null) : IWorkTeamMemberScope
{
    public async Task<WorkTeamMemberLimit?> GetLimitAsync(Guid tenantId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var reach = companyReach is null ? null : await companyReach.GetReachAsync(tenantId, ownerUserId, cancellationToken);
        if (currentUser.UserId == ownerUserId && currentUser.HasPermission(OrgStructurePermissions.TeamMembersFromAnyUnit))
        {
            return await CompanyLimitAsync(tenantId, ownerUserId, reach, cancellationToken);
        }

        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        if (assignments.FirstOrDefault(a => a.UserId == ownerUserId)?.UnitId is not { } unitId)
        {
            return await CompanyLimitAsync(tenantId, ownerUserId, reach, cancellationToken);
        }

        // «The same deputy»: people whose unit has the same domain as the builder's (with no nesting, the same unit).
        var graph = await (graphs ?? new OrgGraphProvider(units, repository)).GetAsync(tenantId, cancellationToken);
        var domain = graph.DomainOf(unitId);
        var allowed = assignments.Where(a => a.UnitId is { } u && graph.DomainOf(u) == domain).Select(a => a.UserId).Append(ownerUserId)
            .Where(userId => reach is null || reach.Contains(userId))
            .ToHashSet();
        var unit = await units.GetByIdAsync(unitId, cancellationToken);
        return new WorkTeamMemberLimit(allowed, unit?.Name ?? string.Empty);
    }

    /// <summary>The company limit alone: its people, named after the company (or none when not limited).</summary>
    private async Task<WorkTeamMemberLimit?> CompanyLimitAsync(Guid tenantId, Guid ownerUserId, UserReach? reach, CancellationToken cancellationToken)
    {
        if (reach is null)
        {
            return null;
        }

        var mine = await companyReach!.GetCompaniesAsync(tenantId, ownerUserId, cancellationToken) ?? new HashSet<Guid>();
        var names = (await repository.ListCompaniesAsync(tenantId, cancellationToken))
            .Where(company => mine.Contains(company.Id))
            .Select(company => company.Name)
            .ToList();
        return new WorkTeamMemberLimit(reach.UserIds, string.Join("، ", names), IsCompany: true);
    }
}
