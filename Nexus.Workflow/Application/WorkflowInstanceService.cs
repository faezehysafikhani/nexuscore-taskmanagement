using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.Workflow.Application;

/// <summary>
/// The delegation parameters are optional: without a delegation repository the approval center and
/// decisions behave exactly as they did before delegation existed.
/// </summary>
public sealed class WorkflowInstanceService(
    IWorkflowInstanceRepository repository,
    IWorkflowUnitOfWork unitOfWork,
    IWorkflowDefinitionRepository? definitionRepository = null,
    IWorkflowDelegationRepository? delegationRepository = null,
    TimeProvider? timeProvider = null) : IWorkflowInstanceService
{
    private DateOnly Today => DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    public async Task<Result<IReadOnlyList<WorkflowInstanceDto>>> ListPendingForApproverAsync(Guid tenantId, Guid approverUserId, CancellationToken cancellationToken)
    {
        // Besides their own, a substitute sees the pending approvals of whoever they stand in for today.
        var delegations = delegationRepository is null
            ? []
            : await delegationRepository.ListActiveForDelegateAsync(tenantId, approverUserId, Today, cancellationToken);

        var instances = delegations.Count == 0
            ? await repository.ListPendingForApproverAsync(tenantId, approverUserId, cancellationToken)
            : await repository.ListPendingForApproverAsync(
                tenantId, approverUserId, delegations.Select(d => new ActingFor(d.DelegatorUserId, d.SubjectType)).ToList(), cancellationToken);

        var approvers = await CurrentApproversAsync(instances, cancellationToken);
        return Result.Success<IReadOnlyList<WorkflowInstanceDto>>(instances.Select(instance =>
        {
            var approver = approvers.GetValueOrDefault(instance.Id);
            var viaDelegation = approver is not null && approver != approverUserId
                && delegations.Any(d => d.DelegatorUserId == approver && d.Covers(instance.SubjectType));
            return ToDto(instance, approver, viaDelegation ? approver : null);
        }).ToList());
    }

    public async Task<Result<WorkflowInstanceDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var instance = await repository.GetByIdAsync(id, cancellationToken);
        if (instance is null)
        {
            return Result.Failure<WorkflowInstanceDto>(Error.NotFound("Workflow instance not found."));
        }

        var approvers = await CurrentApproversAsync([instance], cancellationToken);
        return Result.Success(ToDto(instance, approvers.GetValueOrDefault(instance.Id), null));
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

        // Deciding as a substitute is recorded: the step's designated approver, when the decider is not them
        // but holds a delegation from them that covers this kind of approval today. Anyone else with the
        // approve permission can still decide, as before - this only adds the record of on whose behalf.
        var designated = (await CurrentApproversAsync([instance], cancellationToken)).GetValueOrDefault(instance.Id);
        Guid? onBehalfOf = null;
        if (designated is { } approver && approver != decidedByUserId && delegationRepository is not null)
        {
            var delegations = await delegationRepository.ListActiveForDelegateAsync(instance.TenantId, decidedByUserId, Today, cancellationToken);
            if (delegations.Any(d => d.DelegatorUserId == approver && d.Covers(instance.SubjectType)))
            {
                onBehalfOf = approver;
            }
        }

        // Queues ApprovalGranted/ApprovalRejected (once the instance concludes) as a domain
        // event on the entity; DomainEventDispatchInterceptor dispatches it automatically as
        // part of SaveChangesAsync below, so the module owning the subject (e.g. Risk, Project)
        // reacts without this service knowing who is listening.
        instance.Decide(decidedByUserId, approved, request.Comment, onBehalfOf);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(instance, null, null));
    }

    /// <summary>The designated approver of each instance's current step (null when the step has none or the definition is gone).</summary>
    private async Task<Dictionary<Guid, Guid?>> CurrentApproversAsync(IReadOnlyCollection<WorkflowInstance> instances, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Guid?>();
        if (definitionRepository is null)
        {
            return result;
        }

        var definitions = new Dictionary<Guid, WorkflowDefinition?>();
        foreach (var instance in instances)
        {
            if (!definitions.TryGetValue(instance.WorkflowDefinitionId, out var definition))
            {
                definition = await definitionRepository.GetByIdAsync(instance.WorkflowDefinitionId, cancellationToken);
                definitions[instance.WorkflowDefinitionId] = definition;
            }

            result[instance.Id] = definition?.Steps.SingleOrDefault(step => step.Order == instance.CurrentStepOrder)?.ApproverUserId;
        }

        return result;
    }

    private static WorkflowInstanceDto ToDto(WorkflowInstance instance, Guid? currentApprover, Guid? delegatedFrom) => new(
        instance.Id, instance.TenantId, instance.WorkflowDefinitionId, instance.SubjectType, instance.SubjectId,
        instance.TotalSteps, instance.CurrentStepOrder, instance.Status,
        instance.Decisions.OrderBy(d => d.StepOrder)
            .Select(d => new WorkflowDecisionDto(d.Id, d.StepOrder, d.DecidedByUserId, d.Approved, d.Comment, d.DecidedAtUtc, d.OnBehalfOfUserId))
            .ToList(),
        currentApprover, delegatedFrom);
}
