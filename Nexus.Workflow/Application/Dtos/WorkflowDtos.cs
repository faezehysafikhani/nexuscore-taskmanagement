using Nexus.Workflow.Domain;

namespace Nexus.Workflow.Application.Dtos;

public sealed record WorkflowStepDto(Guid Id, int Order, string Name, Guid? ApproverUserId, Guid? ApproverRoleId, string? ApproverKey = null, bool IsOptional = false);

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

public sealed record AddWorkflowStepRequest(string Name, Guid? ApproverUserId, Guid? ApproverRoleId, string? ApproverKey = null, bool IsOptional = false);

/// <summary>Saves the whole approval chain of one definition: the definition for (SubjectType, ScopeType, ScopeId) is
/// created, or brought back if it was reset, and its steps become exactly <paramref name="Steps"/> (an empty chain
/// means "no approval needed" there). ScopeType omitted = the General definition.</summary>
public sealed record SetWorkflowStepsRequest(
    Guid TenantId, string SubjectType, string? ScopeType, Guid? ScopeId, string? Name, IReadOnlyList<AddWorkflowStepRequest> Steps);

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
    Guid? DelegatedFromUserId = null,
    IReadOnlyList<WorkflowInstanceStepDto>? Steps = null,
    DateTimeOffset? CreatedAtUtc = null,
    Guid? RequestedByUserId = null);

/// <summary>A step as this approval runs it: the title and the person it was resolved to when the approval started.</summary>
public sealed record WorkflowInstanceStepDto(int Order, string Name, Guid? ApproverUserId);

public sealed record DecideWorkflowInstanceRequest(string? Comment);

public sealed record WorkflowDelegationDto(
    Guid Id, Guid TenantId, Guid DelegatorUserId, Guid DelegateUserId, DateOnly StartDate, DateOnly EndDate,
    string? SubjectType, string? Reason, bool IsRevoked, bool IsActiveNow);

/// <summary>DelegatorUserId omitted = the caller (anyone may delegate their own approvals); naming someone else needs Workflow.Configure.
/// SubjectType omitted = every kind of approval.</summary>
public sealed record CreateWorkflowDelegationRequest(
    Guid DelegateUserId, DateOnly StartDate, DateOnly EndDate, string? SubjectType = null, string? Reason = null, Guid? DelegatorUserId = null);

/// <summary>The approvals started for these subjects of one kind (for example the risks of a project): each subject's history in one call.</summary>
public sealed record ListInstancesForSubjectsRequest(string SubjectType, IReadOnlyList<Guid> SubjectIds);
