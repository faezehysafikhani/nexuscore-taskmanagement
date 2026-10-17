using Nexus.Organization.Application;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// With the organization structure, the builder of a work team adds the people of their own
/// deputy only - unless they hold <see cref="OrgStructurePermissions.TeamMembersFromAnyUnit"/>.
/// A builder in no deputy (directly under a company, or with no place yet) is not limited.
/// </summary>
public sealed class DeputyWorkTeamMemberScope(
    IOrgStructureRepository repository,
    IOrganizationUnitRepository units,
    ICurrentUserContext currentUser) : IWorkTeamMemberScope
{
    public async Task<WorkTeamMemberLimit?> GetLimitAsync(Guid tenantId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId == ownerUserId && currentUser.HasPermission(OrgStructurePermissions.TeamMembersFromAnyUnit))
        {
            return null;
        }

        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        if (assignments.FirstOrDefault(a => a.UserId == ownerUserId)?.UnitId is not { } unitId)
        {
            return null;
        }

        var allowed = assignments.Where(a => a.UnitId == unitId).Select(a => a.UserId).Append(ownerUserId).ToHashSet();
        var unit = await units.GetByIdAsync(unitId, cancellationToken);
        return new WorkTeamMemberLimit(allowed, unit?.Name ?? string.Empty);
    }
}
