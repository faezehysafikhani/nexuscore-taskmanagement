using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Agile.Application.Dtos;

public sealed record AgileTaskStatusSubmissionDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid TaskId, AgileTaskStatus PreviousStatus, AgileTaskStatus SubmittedStatus,
    string? Comment, ApprovalStatus ApprovalStatus, Guid? SubmittedByUserId, DateTimeOffset SubmittedAtUtc);

public sealed record SubmitAgileTaskStatusRequest(Guid TenantId, Guid ProjectId, Guid TaskId, AgileTaskStatus Status, string? Comment);

/// <summary>Several projects at once (the screens that show every project's items need them in one call).</summary>
public sealed record ListAgileForProjectsRequest(IReadOnlyList<Guid> ProjectIds);

/// <summary>Makes the project have exactly this many sprints, numbered 1..Count: missing ones are created (planned), and surplus ones
/// are removed from the end while they are planned and hold no tasks.</summary>
public sealed record SetSprintCountRequest(Guid TenantId, Guid ProjectId, int Count);
