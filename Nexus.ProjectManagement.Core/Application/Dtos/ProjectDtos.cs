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

/// <summary>Code omitted (or blank) = the next free automatic code ("P0001", "P0002"...) is assigned.</summary>
public sealed record CreateProjectRequest(
    Guid TenantId,
    string Name,
    string? Code,
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

/// <summary>The lifecycle status to move a project to. Archived is reached through the archive call, not this one.</summary>
public sealed record ChangeProjectStatusRequest(ProjectStatus Status);

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
    bool SortDescending = true,
    Guid? WorkCalendarId = null);

public enum ProjectSortBy
{
    Name,
    Code,
    StartDate,
    EndDate,
    Status,
    CreatedAtUtc
}
