using Nexus.ProjectManagement.Core.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Core.Application.Dtos;

public sealed record ProjectDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Code,
    ProjectType Type,
    ProjectStatus Status,
    ApprovalStatus ApprovalStatus,
    Guid? OwnerUserId,
    Guid? ManagerUserId,
    Guid? OrganizationUnitId,
    Guid? WorkCalendarId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal? Cost,
    string? Goal,
    string? Requirements,
    string? Constraints,
    string? Assumptions,
    string? Description,
    string? Charter,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateProjectRequest(
    Guid TenantId,
    string Name,
    string Code,
    ProjectType Type,
    Guid? ManagerUserId,
    Guid? OwnerUserId,
    Guid? OrganizationUnitId,
    Guid? WorkCalendarId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal? Cost,
    string? Goal,
    string? Requirements,
    string? Constraints,
    string? Assumptions,
    string? Description,
    string? Charter);

public sealed record UpdateProjectRequest(
    string Name,
    string Code,
    Guid? ManagerUserId,
    Guid? OwnerUserId,
    Guid? OrganizationUnitId,
    Guid? WorkCalendarId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal? Cost,
    string? Goal,
    string? Requirements,
    string? Constraints,
    string? Assumptions,
    string? Description,
    string? Charter);

public sealed record ListProjectsRequest(
    Guid TenantId,
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    ProjectType? Type = null,
    ProjectStatus? Status = null,
    Guid? OrganizationUnitId = null,
    Guid? ManagerUserId = null,
    ProjectSortBy SortBy = ProjectSortBy.CreatedAtUtc,
    bool SortDescending = true);

public enum ProjectSortBy
{
    Name,
    Code,
    StartDate,
    EndDate,
    Status,
    CreatedAtUtc
}
