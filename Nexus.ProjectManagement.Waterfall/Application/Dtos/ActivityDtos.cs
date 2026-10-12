using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

public sealed record ActivityDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid? ParentActivityId,
    string Name, string? Description, Guid? DeliverableId, Guid? ResponsibleUserId, Guid? ApproverUserId,
    DateOnly? StartDate, DateOnly? EndDate, int? DurationDays, decimal? ManHours, decimal Weight,
    decimal PlannedProgress, decimal ActualProgress, ApprovalStatus ApprovalStatus,
    bool IsMilestone = false,
    decimal? Cost = null, ActivityLevel Importance = ActivityLevel.Medium, ActivityLevel Complexity = ActivityLevel.Medium);

/// <summary>Cost, Importance and Complexity are optional: left out, a new activity gets no cost and medium levels.</summary>
public sealed record CreateActivityRequest(
    Guid TenantId, Guid ProjectId, Guid? ParentActivityId, string Name, string? Description,
    Guid? DeliverableId, Guid? ResponsibleUserId, Guid? ApproverUserId,
    DateOnly? StartDate, DateOnly? EndDate, int? DurationDays, decimal? ManHours, decimal Weight,
    bool? IsMilestone = null,
    decimal? Cost = null, ActivityLevel? Importance = null, ActivityLevel? Complexity = null);

/// <summary>Cost, Importance and Complexity left out (null) stay as they are.</summary>
public sealed record UpdateActivityRequest(
    Guid? ParentActivityId, string Name, string? Description,
    Guid? DeliverableId, Guid? ResponsibleUserId, Guid? ApproverUserId,
    DateOnly? StartDate, DateOnly? EndDate, int? DurationDays, decimal? ManHours, decimal Weight,
    bool? IsMilestone = null,
    decimal? Cost = null, ActivityLevel? Importance = null, ActivityLevel? Complexity = null);

public sealed record UpdateActivityProgressRequest(decimal PlannedProgress, decimal ActualProgress);
