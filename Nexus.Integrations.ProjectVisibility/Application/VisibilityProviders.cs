using Nexus.Organization.Application;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Team.Application;
using Nexus.Workflow.Application;
using Nexus.Workflow.Domain;

namespace Nexus.Integrations.ProjectVisibility.Application;

/// <summary>A project's team members can see the project (and, through it, the project's actions).</summary>
public sealed class TeamMembershipVisibilityProvider(ITeamRepository? teamRepository = null) : IVisibilityProvider
{
    public async Task<VisibilityGrant> GetGrantAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (teamRepository is null)
        {
            return VisibilityGrant.None;
        }

        var projects = await teamRepository.ListProjectIdsForUserAsync(tenantId, userId, cancellationToken);
        return new VisibilityGrant(projects.ToHashSet(), new HashSet<Guid>(), new HashSet<Guid>());
    }
}

/// <summary>
/// The manager of an organisation unit sees the work of that unit and of every unit below it - so
/// whoever manages a top-level unit (the root of a branch of the chart) sees the whole branch. An
/// inactive unit still counts: its history does not disappear from its manager.
/// </summary>
public sealed class OrganizationUnitManagerVisibilityProvider(IOrganizationUnitRepository? organizationRepository = null) : IVisibilityProvider
{
    public async Task<VisibilityGrant> GetGrantAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (organizationRepository is null)
        {
            return VisibilityGrant.None;
        }

        var units = await organizationRepository.ListAsync(tenantId, cancellationToken);
        var childrenOf = units.Where(u => u.ParentId is not null).ToLookup(u => u.ParentId!.Value, u => u.Id);

        var visible = new HashSet<Guid>();
        var stack = new Stack<Guid>(units.Where(u => u.ManagerUserId == userId).Select(u => u.Id));
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visible.Add(id))
            {
                continue; // already walked - also what stops a parent chain that loops
            }

            foreach (var child in childrenOf[id])
            {
                stack.Push(child);
            }
        }

        return new VisibilityGrant(new HashSet<Guid>(), new HashSet<Guid>(), visible);
    }
}

/// <summary>
/// Whoever has to approve a project or an action - including as a substitute - can see it while it
/// waits for them. Only the subject types that are themselves a project or an action carry over:
/// a risk or a document awaiting approval does not say which project it belongs to here.
/// </summary>
public sealed class PendingApprovalVisibilityProvider(IWorkflowInstanceService? workflowService = null) : IVisibilityProvider
{
    public const string ProjectSubjectType = "Project";
    public const string ActionSubjectType = "Action";

    public async Task<VisibilityGrant> GetGrantAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (workflowService is null)
        {
            return VisibilityGrant.None;
        }

        var pending = await workflowService.ListPendingForApproverAsync(tenantId, userId, cancellationToken);
        if (!pending.IsSuccess)
        {
            return VisibilityGrant.None;
        }

        var open = pending.Value!.Where(i => i.Status == WorkflowInstanceStatus.InProgress).ToList();
        return new VisibilityGrant(
            open.Where(i => i.SubjectType == ProjectSubjectType).Select(i => i.SubjectId).ToHashSet(),
            open.Where(i => i.SubjectType == ActionSubjectType).Select(i => i.SubjectId).ToHashSet(),
            new HashSet<Guid>());
    }
}
