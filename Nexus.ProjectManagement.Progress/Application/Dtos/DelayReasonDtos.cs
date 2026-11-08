using Nexus.ProjectManagement.Progress.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Progress.Application.Dtos;

public sealed record DelayReasonDto(
    Guid Id, Guid TenantId, Guid ProjectId, DateOnly RegisterDate, DelayRootCause RootCause, string Description,
    int? TimeImpactDays, decimal? CostImpact, string? CorrectiveAction, ApprovalStatus ApprovalStatus,
    Guid? CreatedByUserId);

public sealed record CreateDelayReasonRequest(
    Guid TenantId, Guid ProjectId, DateOnly RegisterDate, DelayRootCause RootCause, string Description,
    int? TimeImpactDays, decimal? CostImpact, string? CorrectiveAction);

public sealed record UpdateDelayReasonRequest(
    DateOnly RegisterDate, DelayRootCause RootCause, string Description,
    int? TimeImpactDays, decimal? CostImpact, string? CorrectiveAction);

/// <summary>Several projects at once (the screens that show every project's items need them in one call).</summary>
public sealed record ListDelayReasonsForProjectsRequest(IReadOnlyList<Guid> ProjectIds);
