using Nexus.Actions.Application;
using Nexus.Portfolio.Application.Dtos;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
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
    IActionItemRepository actionRepository) : IPortfolioService
{
    public async Task<Result<PortfolioResultDto>> GetPortfolioAsync(PortfolioQuery query, CancellationToken cancellationToken)
    {
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
            .Where(project => query.ViewAll || project.OwnerUserId == query.CurrentUserId || project.ManagerUserId == query.CurrentUserId)
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
            .Where(action => query.ViewAll || action.OwnerUserId == query.CurrentUserId || action.ResponsibleUserId == query.CurrentUserId)
            .Select(action => new PortfolioActionItem(
                action.Id, action.Title, action.Status.ToString(), action.OrganizationUnitId,
                action.ResponsibleUserId, action.OwnerUserId, action.ApprovalStatus.ToString(),
                action.StartDate, action.EndDate, action.Priority.ToString()))
            .ToList();

        return Result.Success(new PortfolioResultDto(projects, actionItems));
    }

    private static bool EqualsIgnoreCase(string? requested, string actual) =>
        string.Equals(requested, actual, StringComparison.OrdinalIgnoreCase);
}
