using Nexus.Workflow.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Workflow.Application;

/// <summary>The "Approval Center": pending approvals plus approve/reject decisions.</summary>
public interface IWorkflowInstanceService
{
    Task<Result<IReadOnlyList<WorkflowInstanceDto>>> ListPendingForApproverAsync(Guid tenantId, Guid approverUserId, CancellationToken cancellationToken);
    Task<Result<WorkflowInstanceDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<WorkflowInstanceDto>> ApproveAsync(Guid id, Guid decidedByUserId, DecideWorkflowInstanceRequest request, CancellationToken cancellationToken);
    Task<Result<WorkflowInstanceDto>> RejectAsync(Guid id, Guid decidedByUserId, DecideWorkflowInstanceRequest request, CancellationToken cancellationToken);
}
