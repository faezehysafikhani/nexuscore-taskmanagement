using Nexus.Workflow.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Workflow.Application;

/// <summary>The "Approval Center": pending approvals plus approve/reject decisions.</summary>
public interface IWorkflowInstanceService
{
    Task<Result<IReadOnlyList<WorkflowInstanceDto>>> ListPendingForApproverAsync(Guid tenantId, Guid approverUserId, CancellationToken cancellationToken);
    Task<Result<WorkflowInstanceDto>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every approval started for the given subjects of one kind, newest first, each with its decisions and (while in progress) its current approver.</summary>
    Task<Result<IReadOnlyList<WorkflowInstanceDto>>> ListForSubjectsAsync(Guid tenantId, ListInstancesForSubjectsRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    Task<Result<WorkflowInstanceDto>> ApproveAsync(Guid id, Guid decidedByUserId, DecideWorkflowInstanceRequest request, CancellationToken cancellationToken);
    Task<Result<WorkflowInstanceDto>> RejectAsync(Guid id, Guid decidedByUserId, DecideWorkflowInstanceRequest request, CancellationToken cancellationToken);

    /// <summary><paramref name="canActForAnyone"/>: the caller may decide a step designated to someone else (they hold
    /// Workflow.Configure). It matters only when WorkflowOptions.RequireDesignatedApprover is on; an implementation
    /// written before it existed simply ignores it.</summary>
    Task<Result<WorkflowInstanceDto>> ApproveAsync(Guid id, Guid decidedByUserId, DecideWorkflowInstanceRequest request, bool canActForAnyone, CancellationToken cancellationToken) =>
        ApproveAsync(id, decidedByUserId, request, cancellationToken);

    /// <summary><paramref name="holdsPermission"/>: the decider holds the approve/reject permission. Without it they may
    /// still decide a step designated to them, or to someone whose approvals they hold by delegation today.</summary>
    Task<Result<WorkflowInstanceDto>> ApproveAsync(Guid id, Guid decidedByUserId, DecideWorkflowInstanceRequest request, bool canActForAnyone, bool holdsPermission, CancellationToken cancellationToken) =>
        ApproveAsync(id, decidedByUserId, request, canActForAnyone, cancellationToken);

    Task<Result<WorkflowInstanceDto>> RejectAsync(Guid id, Guid decidedByUserId, DecideWorkflowInstanceRequest request, bool canActForAnyone, bool holdsPermission, CancellationToken cancellationToken) =>
        RejectAsync(id, decidedByUserId, request, canActForAnyone, cancellationToken);

    Task<Result<WorkflowInstanceDto>> RejectAsync(Guid id, Guid decidedByUserId, DecideWorkflowInstanceRequest request, bool canActForAnyone, CancellationToken cancellationToken) =>
        RejectAsync(id, decidedByUserId, request, cancellationToken);
}
