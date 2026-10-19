using Nexus.Actions.Application;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Deliverables.Application;
using Nexus.ProjectManagement.Kpi.Application;
using Nexus.ProjectManagement.Meetings.Application;
using Nexus.ProjectManagement.Team.Application;

namespace Nexus.Integrations.ProjectDeletion.Application;

/// <summary>
/// Deleting a project: deliverables, KPIs, actions and meetings that belong to it block the delete (they are work
/// someone did); the team list and governance roles go with it (they mean nothing without the project).
/// Every module is optional - one that is not installed is simply not asked.
/// </summary>
public sealed class ProjectDataDeletionParticipant(
    IDeliverableRepository? deliverables = null,
    IKpiRepository? kpis = null,
    IActionItemRepository? actions = null,
    IMeetingRepository? meetings = null,
    ITeamRepository? team = null,
    ITeamUnitOfWork? teamUnitOfWork = null) : IProjectDeletionParticipant
{
    public async Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var found = new List<string>();

        if (deliverables is not null)
        {
            var count = (await deliverables.ListByProjectAsync(projectId, cancellationToken)).Count;
            if (count > 0) found.Add($"{count} deliverable(s)");
        }

        if (kpis is not null)
        {
            var count = (await kpis.ListByProjectAsync(projectId, null, cancellationToken)).Count;
            if (count > 0) found.Add($"{count} KPI(s)");
        }

        if (actions is not null)
        {
            var count = (await actions.ListAsync(tenantId, projectId, cancellationToken)).Count;
            if (count > 0) found.Add($"{count} action(s)");
        }

        if (meetings is not null)
        {
            var count = (await meetings.ListByProjectAsync(projectId, null, cancellationToken)).Count;
            if (count > 0) found.Add($"{count} meeting(s)");
        }

        return found.Count == 0 ? null : string.Join(", ", found);
    }

    public async Task OnProjectDeletedAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        if (team is null || teamUnitOfWork is null)
        {
            return;
        }

        foreach (var member in await team.ListMembersAsync(projectId, cancellationToken))
        {
            await team.RemoveMemberAsync(member, cancellationToken);
        }

        foreach (var role in await team.ListGovernanceRolesAsync(projectId, cancellationToken))
        {
            await team.RemoveGovernanceRoleAsync(role, cancellationToken);
        }

        await teamUnitOfWork.SaveChangesAsync(cancellationToken);
    }
}
