using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Agile.Application.Dtos;

public sealed record AgileTaskDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Title, string? Description, AgileTaskStatus Status,
    Guid? ResponsibleUserId, Guid? ApproverUserId, DateOnly? DueDate, AgileTaskPriority Priority,
    int? SprintNumber, ApprovalStatus ApprovalStatus, int? StoryPoints = null, int Rank = 0);

public sealed record CreateAgileTaskRequest(
    Guid TenantId, Guid ProjectId, string Title, string? Description,
    Guid? ResponsibleUserId, Guid? ApproverUserId, DateOnly? DueDate, AgileTaskPriority Priority, int? SprintNumber,
    int? StoryPoints = null);

public sealed record UpdateAgileTaskRequest(
    string Title, string? Description, Guid? ResponsibleUserId, Guid? ApproverUserId,
    DateOnly? DueDate, AgileTaskPriority Priority, int? SprintNumber,
    int? StoryPoints = null);

public sealed record ChangeAgileTaskStatusRequest(AgileTaskStatus Status);
