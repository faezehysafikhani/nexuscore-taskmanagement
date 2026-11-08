using Nexus.Actions.Application.Dtos;
using Nexus.Actions.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.Actions.Application;

/// <summary>
/// Asking for an action to move to another status and have someone confirm it. Works with or without an approval backend: with none the
/// action takes the status at once, like every other optional approval in these modules.
/// </summary>
public sealed class ActionStatusSubmissionService(
    IActionStatusSubmissionRepository repository,
    IActionItemRepository actionRepository,
    IActionItemService actionService,
    IActionsUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IActionStatusSubmissionService
{
    public const string SubjectType = "ActionStatus";

    public async Task<Result<IReadOnlyList<ActionStatusSubmissionDto>>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken)
    {
        var items = await repository.ListAsync(tenantId, projectId, cancellationToken);
        return Result.Success<IReadOnlyList<ActionStatusSubmissionDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<ActionStatusSubmissionDto>>> ListByActionAsync(Guid actionId, CancellationToken cancellationToken)
    {
        var items = await repository.ListByActionsAsync([actionId], cancellationToken);
        return Result.Success<IReadOnlyList<ActionStatusSubmissionDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<ActionStatusSubmissionDto>> SubmitAsync(SubmitActionStatusRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Status))
        {
            return Result.Failure<ActionStatusSubmissionDto>(Error.Validation("Unknown action status."));
        }

        var action = await actionRepository.GetByIdAsync(request.ActionId, cancellationToken);
        if (action is null)
        {
            return Result.Failure<ActionStatusSubmissionDto>(Error.NotFound("Action not found."));
        }

        if (action.Status == request.Status)
        {
            return Result.Failure<ActionStatusSubmissionDto>(Error.Validation("The action already has that status."));
        }

        var earlier = await repository.ListByActionsAsync([action.Id], cancellationToken);
        if (earlier.Any(s => s.ApprovalStatus == ApprovalStatus.PendingApproval))
        {
            return Result.Failure<ActionStatusSubmissionDto>(Error.Conflict("An earlier status change for this action is still waiting for approval."));
        }

        var submission = new ActionStatusSubmission(
            Guid.NewGuid(), action.TenantId, action.ProjectId, action.Id, action.Status, request.Status, request.Comment);
        await repository.AddAsync(submission, cancellationToken);

        var subject = new ApprovalSubject(
            SubjectType, submission.Id, submission.TenantId,
            ScopeType: action.ProjectId is null ? null : "Project", ScopeId: action.ProjectId,
            Participants: action.ApprovalParticipants());
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);
        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            submission.MarkPendingApproval();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(submission));
        }

        submission.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var moved = await actionService.ChangeStatusAsync(action.Id, new ChangeActionStatusRequest(request.Status), cancellationToken);
        return moved.IsFailure ? Result.Failure<ActionStatusSubmissionDto>(moved.Error) : Result.Success(ToDto(submission));
    }

    private static ActionStatusSubmissionDto ToDto(ActionStatusSubmission s) => new(
        s.Id, s.TenantId, s.ProjectId, s.ActionId, s.PreviousStatus, s.SubmittedStatus, s.Comment, s.ApprovalStatus,
        s.CreatedByUserId, s.CreatedAtUtc);
}
