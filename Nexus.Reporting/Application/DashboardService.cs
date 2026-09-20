using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.Reporting.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Reporting.Application;

/// <summary>
/// Read/orchestration only - reads Project and ActionItem from their own modules' repositories,
/// the same ones Portfolio reads. IProgressService is injected as nullable: AddProjectReporting
/// never requires AddProgressManagement() to have run, so summaries/dashboards still work (with
/// null progress fields) when Progress Management isn't installed - the compile-time
/// ProjectReference exists only for DTOs/the service interface, not a hard runtime dependency.
/// </summary>
public sealed class DashboardService(
    IProjectRepository projectRepository,
    IActionItemRepository actionRepository,
    IProgressService? progressService = null) : IDashboardService
{
    private static readonly ProjectStatus[] RunningProjectStatuses = [ProjectStatus.Active, ProjectStatus.OnHold];
    private static readonly ActionStatus[] RunningActionStatuses = [ActionStatus.Open, ActionStatus.InProgress];

    public async Task<Result<DashboardSummaryDto>> GetSummaryAsync(Guid tenantId, Guid? organizationUnitId, CancellationToken cancellationToken)
    {
        var projectsPage = await projectRepository.ListAsync(
            new ListProjectsRequest(tenantId, PageNumber: 1, PageSize: 1000, OrganizationUnitId: organizationUnitId),
            cancellationToken);
        var projects = projectsPage.Items;

        var actions = await actionRepository.ListAsync(tenantId, projectId: null, cancellationToken);
        var relevantActions = organizationUnitId is null
            ? actions
            : actions.Where(action => action.OrganizationUnitId == organizationUnitId).ToList();

        var summary = new DashboardSummaryDto(
            ProjectCount: projects.Count,
            RunningProjectCount: projects.Count(project => RunningProjectStatuses.Contains(project.Status)),
            ActionCount: relevantActions.Count,
            RunningActionCount: relevantActions.Count(action => RunningActionStatuses.Contains(action.Status)),
            ProjectsByStatus: projects
                .GroupBy(project => project.Status.ToString())
                .Select(group => new CountByKey(group.Key, group.Count()))
                .ToList(),
            ProjectsByOrganizationUnit: projects
                .GroupBy(project => project.OrganizationUnitId?.ToString() ?? "Unassigned")
                .Select(group => new CountByKey(group.Key, group.Count()))
                .ToList(),
            ProjectsByManager: projects
                .GroupBy(project => project.ManagerUserId?.ToString() ?? "Unassigned")
                .Select(group => new CountByKey(group.Key, group.Count()))
                .ToList());

        return Result.Success(summary);
    }

    public async Task<Result<MyDashboardDto>> GetMyDashboardAsync(Guid tenantId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var projectsPage = await projectRepository.ListAsync(
            new ListProjectsRequest(tenantId, PageNumber: 1, PageSize: 1000),
            cancellationToken);
        var actions = await actionRepository.ListAsync(tenantId, projectId: null, cancellationToken);

        var myProjects = projectsPage.Items
            .Where(project => project.OwnerUserId == currentUserId || project.ManagerUserId == currentUserId)
            .ToList();
        var myActions = actions
            .Where(action => action.OwnerUserId == currentUserId || action.ResponsibleUserId == currentUserId)
            .ToList();

        var dashboard = new MyDashboardDto(
            MyRunningProjectCount: myProjects.Count(project => RunningProjectStatuses.Contains(project.Status)),
            MyRunningActionCount: myActions.Count(action => RunningActionStatuses.Contains(action.Status)),
            MyProjectIds: myProjects.Select(project => project.Id).ToList(),
            MyActionIds: myActions.Select(action => action.Id).ToList());

        return Result.Success(dashboard);
    }

    public async Task<Result<ProjectDashboardDto>> GetProjectDashboardAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return Result.Failure<ProjectDashboardDto>(Error.NotFound("Project not found."));
        }

        decimal? latestPlanned = null;
        decimal? latestActual = null;
        decimal? deviation = null;
        string? performanceClassification = null;

        if (progressService is not null)
        {
            var progressResult = await progressService.ListByProjectAsync(projectId, cancellationToken);
            var latest = progressResult.IsSuccess
                ? progressResult.Value?.OrderByDescending(update => update.RegisterDate).FirstOrDefault()
                : null;

            if (latest is not null)
            {
                latestPlanned = latest.PlannedProgress;
                latestActual = latest.ActualProgress;
                deviation = latest.Deviation;
                performanceClassification = latest.PerformanceClassification.ToString();
            }
        }

        var dashboard = new ProjectDashboardDto(
            project.Id, project.Name, project.Status.ToString(),
            latestPlanned, latestActual, deviation, performanceClassification);

        return Result.Success(dashboard);
    }
}
