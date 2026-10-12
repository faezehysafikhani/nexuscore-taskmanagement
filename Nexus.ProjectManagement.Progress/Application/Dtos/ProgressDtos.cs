using Nexus.ProjectManagement.Progress.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Progress.Application.Dtos;

public sealed record ProgressUpdateDto(
    Guid Id, Guid TenantId, Guid ProjectId, string? StatusDescription, DateOnly RegisterDate,
    decimal PlannedProgress, decimal ActualProgress, decimal? ConfirmedProgress, string? DelayReasons,
    decimal Deviation, PerformanceClassification PerformanceClassification, ApprovalStatus ApprovalStatus,
    Guid? CreatedByUserId);

public sealed record CreateProgressUpdateRequest(
    Guid TenantId, Guid ProjectId, string? StatusDescription, DateOnly RegisterDate,
    decimal PlannedProgress, decimal ActualProgress, string? DelayReasons);

public sealed record UpdateProgressUpdateRequest(
    string? StatusDescription, decimal PlannedProgress, decimal ActualProgress, string? DelayReasons);

/// <summary>Several projects at once (the screens that show every project's items need them in one call).</summary>
public sealed record ListProgressUpdatesForProjectsRequest(IReadOnlyList<Guid> ProjectIds);
