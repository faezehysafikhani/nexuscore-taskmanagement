using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

/// <summary>
/// Optional extension point: whether a reviewer who rejects a progress report may still confirm a progress figure of their own.
/// With none registered it is always allowed, which is how it behaved before the policy existed.
/// </summary>
public interface IProgressRejectionPolicy
{
    Task<bool> AllowsConfirmedProgressAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Reporting an activity's progress for confirmation. Works with or without an approval backend: with none, the report is
/// confirmed at once, like every other optional approval in the project-management modules.
/// </summary>
public sealed class ActivityProgressSubmissionService(
    IActivityProgressSubmissionRepository repository,
    IActivityRepository activityRepository,
    IWaterfallUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester,
    IProgressRejectionPolicy? progressRejectionPolicy = null) : IActivityProgressSubmissionService
{
    public const string SubjectType = "ActivityProgress";

    public async Task<Result<IReadOnlyList<ActivityProgressSubmissionDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await ListForProjectsAsync(new ListProgressSubmissionsForProjectsRequest([projectId]), cancellationToken);

    public async Task<Result<IReadOnlyList<ActivityProgressSubmissionDto>>> ListForProjectsAsync(
        ListProgressSubmissionsForProjectsRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ProjectIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<ActivityProgressSubmissionDto>>([]);
        }

        var items = await repository.ListByProjectsAsync(ids, cancellationToken);
        return Result.Success<IReadOnlyList<ActivityProgressSubmissionDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<ActivityProgressSubmissionDto>>> ListByActivityAsync(Guid activityId, CancellationToken cancellationToken)
    {
        var items = await repository.ListByActivityAsync(activityId, cancellationToken);
        return Result.Success<IReadOnlyList<ActivityProgressSubmissionDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<ActivityProgressSubmissionDto>> SubmitAsync(SubmitActivityProgressRequest request, CancellationToken cancellationToken)
    {
        if (request.Progress is < 0 or > 100)
        {
            return Result.Failure<ActivityProgressSubmissionDto>(Error.Validation("Progress must be between 0 and 100."));
        }

        var activity = await activityRepository.GetByIdAsync(request.ActivityId, cancellationToken);
        if (activity is null || activity.ProjectId != request.ProjectId)
        {
            return Result.Failure<ActivityProgressSubmissionDto>(Error.NotFound("Activity not found."));
        }

        var all = await activityRepository.ListByProjectAsync(request.ProjectId, cancellationToken);
        if (all.Any(a => a.ParentActivityId == activity.Id))
        {
            return Result.Failure<ActivityProgressSubmissionDto>(Error.Validation("Progress is reported on activities without sub-activities; a group's progress follows from what is inside it."));
        }

        var earlier = await repository.ListByActivityAsync(activity.Id, cancellationToken);
        if (earlier.Any(s => s.ApprovalStatus == ApprovalStatus.PendingApproval))
        {
            return Result.Failure<ActivityProgressSubmissionDto>(Error.Conflict("An earlier progress report for this activity is still waiting for approval."));
        }

        var submission = new ActivityProgressSubmission(
            Guid.NewGuid(), request.TenantId, request.ProjectId, activity.Id, request.Progress, request.Comment, request.DocumentName);
        await repository.AddAsync(submission, cancellationToken);

        var subject = new ApprovalSubject(SubjectType, submission.Id, submission.TenantId, ScopeType: "Project", ScopeId: submission.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);
        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            submission.MarkPendingApproval();
        }
        else
        {
            submission.Approve();
            activity.UpdateProgress(activity.PlannedProgress, submission.EffectiveProgress);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(submission));
    }

    public async Task<Result<ActivityProgressSubmissionDto>> SetConfirmedProgressAsync(
        Guid id, SetConfirmedProgressRequest request, CancellationToken cancellationToken)
    {
        if (request.Progress is < 0 or > 100)
        {
            return Result.Failure<ActivityProgressSubmissionDto>(Error.Validation("Progress must be between 0 and 100."));
        }

        var submission = await repository.GetByIdAsync(id, cancellationToken);
        if (submission is null)
        {
            return Result.Failure<ActivityProgressSubmissionDto>(Error.NotFound("Progress report not found."));
        }

        if (submission.ApprovalStatus != ApprovalStatus.PendingApproval)
        {
            return Result.Failure<ActivityProgressSubmissionDto>(Error.Conflict("Only a report that is waiting for approval can have its confirmed progress set."));
        }

        if (progressRejectionPolicy is not null && !await progressRejectionPolicy.AllowsConfirmedProgressAsync(submission.TenantId, cancellationToken))
        {
            return Result.Failure<ActivityProgressSubmissionDto>(Error.Forbidden("Confirming a progress figure when rejecting a report is switched off."));
        }

        submission.SetApprovedProgress(request.Progress);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(submission));
    }

    private static ActivityProgressSubmissionDto ToDto(ActivityProgressSubmission s) => new(
        s.Id, s.TenantId, s.ProjectId, s.ActivityId, s.SubmittedProgress, s.ApprovedProgress, s.IsPartialRejection,
        s.Comment, s.DocumentName, s.ApprovalStatus, s.CreatedByUserId, s.CreatedAtUtc);
}
