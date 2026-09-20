using Nexus.Workflow.Domain;

namespace Nexus.Workflow.Application.Dtos;

public sealed record WorkflowStepDto(Guid Id, int Order, string Name, Guid? ApproverUserId, Guid? ApproverRoleId);

public sealed record WorkflowDefinitionDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string SubjectType,
    string ScopeType,
    Guid? ScopeId,
    bool IsActive,
    IReadOnlyList<WorkflowStepDto> Steps);

public sealed record CreateWorkflowDefinitionRequest(Guid TenantId, string Name, string SubjectType, string? ScopeType, Guid? ScopeId);

public sealed record AddWorkflowStepRequest(string Name, Guid? ApproverUserId, Guid? ApproverRoleId);

public sealed record MoveWorkflowStepRequest(int NewOrder);

public sealed record WorkflowDecisionDto(Guid Id, int StepOrder, Guid DecidedByUserId, bool Approved, string? Comment, DateTimeOffset DecidedAtUtc);

public sealed record WorkflowInstanceDto(
    Guid Id,
    Guid TenantId,
    Guid WorkflowDefinitionId,
    string SubjectType,
    Guid SubjectId,
    int TotalSteps,
    int CurrentStepOrder,
    WorkflowInstanceStatus Status,
    IReadOnlyList<WorkflowDecisionDto> Decisions);

public sealed record DecideWorkflowInstanceRequest(string? Comment);
