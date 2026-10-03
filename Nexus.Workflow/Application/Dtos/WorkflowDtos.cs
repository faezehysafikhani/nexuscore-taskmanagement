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

public sealed record WorkflowDecisionDto(
    Guid Id, int StepOrder, Guid DecidedByUserId, bool Approved, string? Comment, DateTimeOffset DecidedAtUtc, Guid? OnBehalfOfUserId = null);

public sealed record WorkflowInstanceDto(
    Guid Id,
    Guid TenantId,
    Guid WorkflowDefinitionId,
    string SubjectType,
    Guid SubjectId,
    int TotalSteps,
    int CurrentStepOrder,
    WorkflowInstanceStatus Status,
    IReadOnlyList<WorkflowDecisionDto> Decisions,
    Guid? CurrentApproverUserId = null,
    Guid? DelegatedFromUserId = null);

public sealed record DecideWorkflowInstanceRequest(string? Comment);

public sealed record WorkflowDelegationDto(
    Guid Id, Guid TenantId, Guid DelegatorUserId, Guid DelegateUserId, DateOnly StartDate, DateOnly EndDate,
    string? SubjectType, string? Reason, bool IsRevoked, bool IsActiveNow);

/// <summary>DelegatorUserId omitted = the caller (anyone may delegate their own approvals); naming someone else needs Workflow.Configure.
/// SubjectType omitted = every kind of approval.</summary>
public sealed record CreateWorkflowDelegationRequest(
    Guid DelegateUserId, DateOnly StartDate, DateOnly EndDate, string? SubjectType = null, string? Reason = null, Guid? DelegatorUserId = null);
