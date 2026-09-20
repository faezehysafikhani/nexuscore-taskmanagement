using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

public sealed record ActivityDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid? ParentActivityId,
    string Name, string? Description, Guid? DeliverableId, Guid? ResponsibleUserId, Guid? ApproverUserId,
    DateOnly? StartDate, DateOnly? EndDate, int? DurationDays, decimal? ManHours, decimal Weight,
    decimal PlannedProgress, decimal ActualProgress, ApprovalStatus ApprovalStatus);

public sealed record CreateActivityRequest(
    Guid TenantId, Guid ProjectId, Guid? ParentActivityId, string Name, string? Description,
    Guid? DeliverableId, Guid? ResponsibleUserId, Guid? ApproverUserId,
    DateOnly? StartDate, DateOnly? EndDate, int? DurationDays, decimal? ManHours, decimal Weight);

public sealed record UpdateActivityRequest(
    Guid? ParentActivityId, string Name, string? Description,
    Guid? DeliverableId, Guid? ResponsibleUserId, Guid? ApproverUserId,
    DateOnly? StartDate, DateOnly? EndDate, int? DurationDays, decimal? ManHours, decimal Weight);

public sealed record UpdateActivityProgressRequest(decimal PlannedProgress, decimal ActualProgress);
