using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Progress.Application;

/// <summary>Fully usable without Workflow, using the same optional-approval pattern as
/// <see cref="ProgressService"/>: when no approval backend is installed, submitting a delay
/// reason approves it directly.</summary>
public sealed class DelayReasonService(
    IDelayReasonRepository repository,
    IProgressUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IDelayReasonService
{
    public const string SubjectType = "DelayReason";

    public async Task<Result<IReadOnlyList<DelayReasonDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var reasons = await repository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<DelayReasonDto>>(reasons.Select(ToDto).ToList());
    }

    public async Task<Result<DelayReasonDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var reason = await repository.GetByIdAsync(id, cancellationToken);
        return reason is null
            ? Result.Failure<DelayReasonDto>(Error.NotFound("Delay reason not found."))
            : Result.Success(ToDto(reason));
    }

    public async Task<Result<DelayReasonDto>> CreateAsync(CreateDelayReasonRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<DelayReasonDto>(Error.Validation("Description is required."));
        }

        var reason = new DelayReason(Guid.NewGuid(), request.TenantId, request.ProjectId, request.RegisterDate, request.RootCause, request.Description);
        reason.UpdateDetails(request.RegisterDate, request.RootCause, request.Description, request.TimeImpactDays, request.CostImpact, request.CorrectiveAction);

        await repository.AddAsync(reason, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(reason));
    }

    public async Task<Result<DelayReasonDto>> UpdateAsync(Guid id, UpdateDelayReasonRequest request, CancellationToken cancellationToken)
    {
        var reason = await repository.GetByIdAsync(id, cancellationToken);
        if (reason is null)
        {
            return Result.Failure<DelayReasonDto>(Error.NotFound("Delay reason not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<DelayReasonDto>(Error.Validation("Description is required."));
        }

        reason.UpdateDetails(request.RegisterDate, request.RootCause, request.Description, request.TimeImpactDays, request.CostImpact, request.CorrectiveAction);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(reason));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var reason = await repository.GetByIdAsync(id, cancellationToken);
        if (reason is null)
        {
            return Result.Failure(Error.NotFound("Delay reason not found."));
        }

        // An approval request is already open for this delay reason; deleting it now would leave the
        // workflow instance pointing at a subject that no longer exists.
        if (reason.ApprovalStatus == ApprovalStatus.PendingApproval)
        {
            return Result.Failure(Error.Conflict("A delay reason that is pending approval cannot be deleted."));
        }

        await repository.RemoveAsync(reason, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<DelayReasonDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var reason = await repository.GetByIdAsync(id, cancellationToken);
        if (reason is null)
        {
            return Result.Failure<DelayReasonDto>(Error.NotFound("Delay reason not found."));
        }

        var subject = new ApprovalSubject(SubjectType, reason.Id, reason.TenantId, ScopeType: "Project", ScopeId: reason.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            reason.MarkPendingApproval();
        }
        else
        {
            // No Workflow installed: apply the direct-approve business rule immediately.
            reason.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(reason));
    }

    private static DelayReasonDto ToDto(DelayReason reason) => new(
        reason.Id, reason.TenantId, reason.ProjectId, reason.RegisterDate, reason.RootCause, reason.Description,
        reason.TimeImpactDays, reason.CostImpact, reason.CorrectiveAction, reason.ApprovalStatus,
        reason.CreatedByUserId);
}
