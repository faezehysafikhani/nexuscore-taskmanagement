using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.EventHandlers;

public sealed class ActivityApprovalGrantedHandler(IActivityRepository repository, IWaterfallUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "WaterfallActivity")
        {
            return;
        }

        var activity = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (activity is null)
        {
            return;
        }

        activity.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ActivityApprovalRejectedHandler(IActivityRepository repository, IWaterfallUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "WaterfallActivity")
        {
            return;
        }

        var activity = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (activity is null)
        {
            return;
        }

        activity.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>A progress report was confirmed: the activity takes the confirmed figure.</summary>
public sealed class ActivityProgressApprovalGrantedHandler(
    IActivityProgressSubmissionRepository submissions, IActivityRepository activities, IWaterfallUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != ActivityProgressSubmissionService.SubjectType)
        {
            return;
        }

        var submission = await submissions.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (submission is null)
        {
            return;
        }

        submission.Approve();
        var activity = await activities.GetByIdAsync(submission.ActivityId, cancellationToken);
        activity?.UpdateProgress(activity.PlannedProgress, submission.EffectiveProgress);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>A progress report was refused. If the approver had confirmed a figure first, that figure still becomes the activity's progress.</summary>
public sealed class ActivityProgressApprovalRejectedHandler(
    IActivityProgressSubmissionRepository submissions, IActivityRepository activities, IWaterfallUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != ActivityProgressSubmissionService.SubjectType)
        {
            return;
        }

        var submission = await submissions.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (submission is null)
        {
            return;
        }

        submission.Reject();
        if (submission.ApprovedProgress is { } confirmed)
        {
            var activity = await activities.GetByIdAsync(submission.ActivityId, cancellationToken);
            activity?.UpdateProgress(activity.PlannedProgress, confirmed);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
