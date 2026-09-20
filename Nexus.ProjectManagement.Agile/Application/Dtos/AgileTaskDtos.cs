using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Agile.Application.Dtos;

public sealed record AgileTaskDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Title, string? Description, AgileTaskStatus Status,
    Guid? ResponsibleUserId, Guid? ApproverUserId, DateOnly? DueDate, AgileTaskPriority Priority,
    int? SprintNumber, ApprovalStatus ApprovalStatus);

public sealed record CreateAgileTaskRequest(
    Guid TenantId, Guid ProjectId, string Title, string? Description,
    Guid? ResponsibleUserId, Guid? ApproverUserId, DateOnly? DueDate, AgileTaskPriority Priority, int? SprintNumber);

public sealed record UpdateAgileTaskRequest(
    string Title, string? Description, Guid? ResponsibleUserId, Guid? ApproverUserId,
    DateOnly? DueDate, AgileTaskPriority Priority, int? SprintNumber);

public sealed record ChangeAgileTaskStatusRequest(AgileTaskStatus Status);
