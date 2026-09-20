using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.Workflow.Application;

public sealed class WorkflowInstanceService(
    IWorkflowInstanceRepository repository,
    IWorkflowUnitOfWork unitOfWork) : IWorkflowInstanceService
{
    public async Task<Result<IReadOnlyList<WorkflowInstanceDto>>> ListPendingForApproverAsync(Guid tenantId, Guid approverUserId, CancellationToken cancellationToken)
    {
        var instances = await repository.ListPendingForApproverAsync(tenantId, approverUserId, cancellationToken);
        return Result.Success<IReadOnlyList<WorkflowInstanceDto>>(instances.Select(ToDto).ToList());
    }

    public async Task<Result<WorkflowInstanceDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var instance = await repository.GetByIdAsync(id, cancellationToken);
        return instance is null
            ? Result.Failure<WorkflowInstanceDto>(Error.NotFound("Workflow instance not found."))
            : Result.Success(ToDto(instance));
    }

    public Task<Result<WorkflowInstanceDto>> ApproveAsync(Guid id, Guid decidedByUserId, DecideWorkflowInstanceRequest request, CancellationToken cancellationToken) =>
        DecideAsync(id, decidedByUserId, approved: true, request, cancellationToken);

    public Task<Result<WorkflowInstanceDto>> RejectAsync(Guid id, Guid decidedByUserId, DecideWorkflowInstanceRequest request, CancellationToken cancellationToken) =>
        DecideAsync(id, decidedByUserId, approved: false, request, cancellationToken);

    private async Task<Result<WorkflowInstanceDto>> DecideAsync(Guid id, Guid decidedByUserId, bool approved, DecideWorkflowInstanceRequest request, CancellationToken cancellationToken)
    {
        var instance = await repository.GetByIdAsync(id, cancellationToken);
        if (instance is null)
        {
            return Result.Failure<WorkflowInstanceDto>(Error.NotFound("Workflow instance not found."));
        }

        if (instance.Status != WorkflowInstanceStatus.InProgress)
        {
            return Result.Failure<WorkflowInstanceDto>(Error.Conflict("This workflow instance has already been decided."));
        }

        // Queues ApprovalGranted/ApprovalRejected (once the instance concludes) as a domain
        // event on the entity; DomainEventDispatchInterceptor dispatches it automatically as
        // part of SaveChangesAsync below, so the module owning the subject (e.g. Risk, Project)
        // reacts without this service knowing who is listening.
        instance.Decide(decidedByUserId, approved, request.Comment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(instance));
    }

    private static WorkflowInstanceDto ToDto(WorkflowInstance instance) => new(
        instance.Id, instance.TenantId, instance.WorkflowDefinitionId, instance.SubjectType, instance.SubjectId,
        instance.TotalSteps, instance.CurrentStepOrder, instance.Status,
        instance.Decisions.OrderBy(d => d.StepOrder).Select(d => new WorkflowDecisionDto(d.Id, d.StepOrder, d.DecidedByUserId, d.Approved, d.Comment, d.DecidedAtUtc)).ToList());
}
