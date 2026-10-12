using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

public sealed record ActivityProgressSubmissionDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid ActivityId, decimal SubmittedProgress, decimal? ApprovedProgress,
    bool IsPartialRejection, string? Comment, string? DocumentName, ApprovalStatus ApprovalStatus,
    Guid? SubmittedByUserId, DateTimeOffset SubmittedAtUtc);

public sealed record SubmitActivityProgressRequest(
    Guid TenantId, Guid ProjectId, Guid ActivityId, decimal Progress, string? Comment, string? DocumentName);

/// <summary>The figure an approver confirms (which may differ from the reported one) before deciding.</summary>
public sealed record SetConfirmedProgressRequest(decimal Progress);

public sealed record ListProgressSubmissionsForProjectsRequest(IReadOnlyList<Guid> ProjectIds);
