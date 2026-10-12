using Nexus.Actions.Application;
using Nexus.Portfolio.Application.Dtos;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.Portfolio.Application;

/// <summary>
/// Owns nothing - reads Project from IProjectRepository and Action from IActionItemRepository,
/// the same repositories their own modules use, then applies the combined portfolio's
/// filtering and visibility rules. Grouping/expand-collapse are left to the client: the
/// fields needed to group (Status, Type/OrganizationUnitId, Manager/Responsible) are all
/// present in the result, and building a grouping tree server-side would just be presentation
/// logic duplicated from the UI.
/// </summary>
public sealed class PortfolioService(
    IProjectRepository projectRepository,
    IActionItemRepository actionRepository,
    IEnumerable<IVisibilityProvider>? visibilityProviders = null) : IPortfolioService
{
    public async Task<Result<PortfolioResultDto>> GetPortfolioAsync(PortfolioQuery query, CancellationToken cancellationToken)
    {
        var grant = await GetGrantAsync(query, cancellationToken);

        var projectsPage = await projectRepository.ListAsync(
            new ListProjectsRequest(query.TenantId, PageNumber: 1, PageSize: 1000, Search: query.Search, OrganizationUnitId: query.OrganizationUnitId),
            cancellationToken);

        var actions = await actionRepository.ListAsync(query.TenantId, projectId: null, cancellationToken);

        var includeProjects = !EqualsIgnoreCase(query.Type, "Action") && query.Priority is null;
        var includeActions = query.Type is null || EqualsIgnoreCase(query.Type, "Action");

        var projects = !includeProjects ? [] : projectsPage.Items
            .Where(project => query.Type is null || EqualsIgnoreCase(query.Type, project.Type.ToString()))
            .Where(project => query.Status is null || project.Status.ToString() == query.Status)
            .Where(project => query.ApprovalStatus is null || project.ApprovalStatus.ToString() == query.ApprovalStatus)
            .Where(project => query.InvolvedUserId is null || project.OwnerUserId == query.InvolvedUserId || project.ManagerUserId == query.InvolvedUserId)
            .Where(project => query.ViewAll || project.OwnerUserId == query.CurrentUserId || project.ManagerUserId == query.CurrentUserId
                || grant.ProjectIds.Contains(project.Id)
                || (project.OrganizationUnitId is { } unit && grant.OrganizationUnitIds.Contains(unit)))
            .Select(project => new PortfolioProjectItem(
                project.Id, project.Name, project.Code, project.Type.ToString(), project.Status.ToString(),
                project.OrganizationUnitId, project.ManagerUserId, project.OwnerUserId, project.ApprovalStatus.ToString(),
                project.StartDate, project.EndDate))
            .ToList();

        var actionItems = !includeActions ? [] : actions
            .Where(action => string.IsNullOrWhiteSpace(query.Search) || action.Title.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .Where(action => query.ApprovalStatus is null || action.ApprovalStatus.ToString() == query.ApprovalStatus)
            .Where(action => query.Priority is null || EqualsIgnoreCase(query.Priority, action.Priority.ToString()))
            .Where(action => query.InvolvedUserId is null || action.OwnerUserId == query.InvolvedUserId || action.ResponsibleUserId == query.InvolvedUserId)
            .Where(action => query.OrganizationUnitId is null || action.OrganizationUnitId == query.OrganizationUnitId)
            .Where(action => query.Status is null || action.Status.ToString() == query.Status)
            .Where(action => query.ViewAll || action.OwnerUserId == query.CurrentUserId || action.ResponsibleUserId == query.CurrentUserId
                || grant.ActionIds.Contains(action.Id)
                || grant.OrganizationUnitIds.Contains(action.OrganizationUnitId)
                || (action.ProjectId is { } projectId && grant.ProjectIds.Contains(projectId)))
            .Select(action => new PortfolioActionItem(
                action.Id, action.Title, action.Status.ToString(), action.OrganizationUnitId,
                action.ResponsibleUserId, action.OwnerUserId, action.ApprovalStatus.ToString(),
                action.StartDate, action.EndDate, action.Priority.ToString()))
            .ToList();

        return Result.Success(new PortfolioResultDto(projects, actionItems));
    }

    public async Task<Result<IReadOnlyList<ProjectDto>>> GetVisibleProjectsAsync(PortfolioQuery query, bool includeArchived, CancellationToken cancellationToken)
    {
        var grant = await GetGrantAsync(query, cancellationToken);
        var page = await projectRepository.ListAsync(
            new ListProjectsRequest(query.TenantId, PageNumber: 1, PageSize: 1000, Search: query.Search, OrganizationUnitId: query.OrganizationUnitId),
            cancellationToken);

        var projects = page.Items
            .Where(project => includeArchived || project.Status != ProjectStatus.Archived)
            .Where(project => query.Type is null || EqualsIgnoreCase(query.Type, project.Type.ToString()))
            .Where(project => query.Status is null || project.Status.ToString() == query.Status)
            .Where(project => query.ApprovalStatus is null || project.ApprovalStatus.ToString() == query.ApprovalStatus)
            .Where(project => query.InvolvedUserId is null || project.OwnerUserId == query.InvolvedUserId || project.ManagerUserId == query.InvolvedUserId)
            .Where(project => IsVisible(project, query, grant))
            .OrderByDescending(project => project.CreatedAtUtc)
            .Select(ToDto)
            .ToList();

        return Result.Success<IReadOnlyList<ProjectDto>>(projects);
    }

    // Besides what a user owns, manages or is responsible for, any installed visibility providers
    // (team membership, organisation chart, pending approvals...) can show them more. Skipped for
    // ViewAll, which already sees everything.
    private async Task<VisibilityGrant> GetGrantAsync(PortfolioQuery query, CancellationToken cancellationToken)
    {
        if (query.ViewAll || visibilityProviders is null)
        {
            return VisibilityGrant.None;
        }

        var grants = new List<VisibilityGrant>();
        foreach (var provider in visibilityProviders)
        {
            grants.Add(await provider.GetGrantAsync(query.TenantId, query.CurrentUserId, cancellationToken));
        }

        return VisibilityGrant.Union(grants);
    }

    private static bool IsVisible(Project project, PortfolioQuery query, VisibilityGrant grant) =>
        query.ViewAll || project.OwnerUserId == query.CurrentUserId || project.ManagerUserId == query.CurrentUserId
        || grant.ProjectIds.Contains(project.Id)
        || (project.OrganizationUnitId is { } unit && grant.OrganizationUnitIds.Contains(unit));

    private static ProjectDto ToDto(Project project) => new(
        project.Id, project.TenantId, project.Name, project.Code, project.Type, project.Status, project.ApprovalStatus,
        project.OwnerUserId, project.ManagerUserId, project.OrganizationUnitId, project.WorkCalendarId,
        project.StartDate, project.EndDate, project.Cost,
        project.Goal, project.Requirements, project.Constraints, project.Assumptions, project.Description, project.Charter,
        project.CreatedAtUtc);

    private static bool EqualsIgnoreCase(string? requested, string actual) =>
        string.Equals(requested, actual, StringComparison.OrdinalIgnoreCase);
}
