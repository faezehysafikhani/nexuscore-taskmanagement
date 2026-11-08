using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.Workflow.Application;

public sealed class WorkflowDelegationService(
    IWorkflowDelegationRepository repository,
    IWorkflowUnitOfWork unitOfWork,
    TimeProvider? timeProvider = null) : IWorkflowDelegationService
{
    private DateOnly Today => DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    public async Task<Result<IReadOnlyList<WorkflowDelegationDto>>> ListMineAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var delegations = await repository.ListForUserAsync(tenantId, userId, cancellationToken);
        var today = Today;
        return Result.Success<IReadOnlyList<WorkflowDelegationDto>>(delegations.Select(d => ToDto(d, today)).ToList());
    }

    public async Task<Result<WorkflowDelegationDto>> CreateAsync(
        Guid tenantId, Guid currentUserId, bool canManageOthers, CreateWorkflowDelegationRequest request, CancellationToken cancellationToken)
    {
        var delegator = request.DelegatorUserId ?? currentUserId;
        if (delegator != currentUserId && !canManageOthers)
        {
            return Result.Failure<WorkflowDelegationDto>(Error.Forbidden("Only a workflow administrator can delegate someone else's approvals."));
        }

        if (request.DelegateUserId == Guid.Empty || request.DelegateUserId == delegator)
        {
            return Result.Failure<WorkflowDelegationDto>(Error.Validation("Choose someone other than the delegator to stand in."));
        }

        if (request.EndDate < request.StartDate)
        {
            return Result.Failure<WorkflowDelegationDto>(Error.Validation("The end date cannot be before the start date."));
        }

        if (request.EndDate < Today)
        {
            return Result.Failure<WorkflowDelegationDto>(Error.Validation("The delegation has already ended."));
        }

        if (request.SubjectType is { Length: > 80 } || request.Reason is { Length: > 500 })
        {
            return Result.Failure<WorkflowDelegationDto>(Error.Validation("The subject type is at most 80 characters and the reason at most 500."));
        }

        // The same pair cannot be given overlapping, overlapping-scope delegations: it would be unclear which one is in force.
        var subjectType = string.IsNullOrWhiteSpace(request.SubjectType) ? null : request.SubjectType.Trim();
        var existing = await repository.ListLiveBetweenAsync(tenantId, delegator, request.DelegateUserId, cancellationToken);
        if (existing.Any(d => d.StartDate <= request.EndDate && request.StartDate <= d.EndDate
                              && (d.SubjectType is null || subjectType is null || d.SubjectType == subjectType)))
        {
            return Result.Failure<WorkflowDelegationDto>(Error.Conflict("These two people already have an overlapping delegation."));
        }

        var delegation = new WorkflowDelegation(
            Guid.NewGuid(), tenantId, delegator, request.DelegateUserId, request.StartDate, request.EndDate, subjectType, request.Reason);
        await repository.AddAsync(delegation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(delegation, Today));
    }

    public async Task<Result<WorkflowDelegationDto>> RevokeAsync(Guid tenantId, Guid id, Guid currentUserId, bool canManageOthers, CancellationToken cancellationToken)
    {
        var delegation = await repository.GetByIdAsync(id, cancellationToken);
        if (delegation is null || delegation.TenantId != tenantId)
        {
            return Result.Failure<WorkflowDelegationDto>(Error.NotFound("Delegation not found."));
        }

        if (delegation.DelegatorUserId != currentUserId && !canManageOthers)
        {
            return Result.Failure<WorkflowDelegationDto>(Error.Forbidden("Only the delegator or a workflow administrator can revoke a delegation."));
        }

        if (!delegation.IsRevoked)
        {
            delegation.Revoke();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(ToDto(delegation, Today));
    }

    private static WorkflowDelegationDto ToDto(WorkflowDelegation d, DateOnly today) => new(
        d.Id, d.TenantId, d.DelegatorUserId, d.DelegateUserId, d.StartDate, d.EndDate, d.SubjectType, d.Reason, d.IsRevoked, d.IsActiveOn(today));
}
