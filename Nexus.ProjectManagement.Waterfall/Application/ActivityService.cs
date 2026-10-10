using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.Application.Approvals;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public sealed class ActivityService(
    IActivityRepository repository,
    IActivityDependencyRepository dependencyRepository,
    IWaterfallUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester,
    IPlatformService platformService,
    WaterfallOptions? options = null) : IActivityService
{
    public async Task<Result<IReadOnlyList<ActivityDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var activities = await repository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<ActivityDto>>(activities.Select(ToDto).ToList());
    }

    public async Task<Result<ActivityDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var activity = await repository.GetByIdAsync(id, cancellationToken);
        return activity is null
            ? Result.Failure<ActivityDto>(Error.NotFound("Activity not found."))
            : Result.Success(ToDto(activity));
    }

    public async Task<Result<ActivityDto>> CreateAsync(CreateActivityRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<ActivityDto>(Error.Validation("Name is required."));
        }

        if (options?.ValidateActivityHierarchy == true)
        {
            var parentError = await ValidateParentAsync(request.ProjectId, activityId: null, request.ParentActivityId, cancellationToken);
            if (parentError is not null)
            {
                return Result.Failure<ActivityDto>(parentError);
            }
        }

        var activity = new Activity(Guid.NewGuid(), request.TenantId, request.ProjectId, request.Name, request.ParentActivityId);
        activity.UpdateDetails(
            request.Name, request.Description, request.ParentActivityId, request.DeliverableId,
            request.ResponsibleUserId, request.ApproverUserId,
            request.StartDate, request.EndDate, request.DurationDays, request.ManHours, request.Weight);
        if (request.IsMilestone == true)
        {
            activity.SetMilestone(true);
        }

        await repository.AddAsync(activity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(activity));
    }

    public async Task<Result<ActivityDto>> UpdateAsync(Guid id, UpdateActivityRequest request, CancellationToken cancellationToken)
    {
        var activity = await repository.GetByIdAsync(id, cancellationToken);
        if (activity is null)
        {
            return Result.Failure<ActivityDto>(Error.NotFound("Activity not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<ActivityDto>(Error.Validation("Name is required."));
        }

        if (request.ParentActivityId == id)
        {
            return Result.Failure<ActivityDto>(Error.Validation("An activity cannot be its own parent."));
        }

        // Only a changed parent needs checking; editing other fields must keep working on
        // activities that were placed before these rules existed.
        if (options?.ValidateActivityHierarchy == true && request.ParentActivityId != activity.ParentActivityId)
        {
            var parentError = await ValidateParentAsync(activity.ProjectId, id, request.ParentActivityId, cancellationToken);
            if (parentError is not null)
            {
                return Result.Failure<ActivityDto>(parentError);
            }
        }

        if (request.IsMilestone == true)
        {
            var all = await repository.ListByProjectAsync(activity.ProjectId, cancellationToken);
            if (all.Any(a => a.ParentActivityId == id))
            {
                return Result.Failure<ActivityDto>(Error.Validation("An activity with sub-activities cannot be a milestone."));
            }
        }

        activity.UpdateDetails(
            request.Name, request.Description, request.ParentActivityId, request.DeliverableId,
            request.ResponsibleUserId, request.ApproverUserId,
            request.StartDate, request.EndDate, request.DurationDays, request.ManHours, request.Weight);
        if (request.IsMilestone is { } isMilestone && (isMilestone || activity.IsMilestone))
        {
            activity.SetMilestone(isMilestone);
        }
        else if (activity.IsMilestone)
        {
            // Left unspecified on a milestone: UpdateDetails may have changed its dates or duration,
            // and a milestone's duration is always zero with its end on its start.
            activity.SetMilestone(true);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(activity));
    }

    public async Task<Result<ActivityDto>> UpdateProgressAsync(Guid id, UpdateActivityProgressRequest request, CancellationToken cancellationToken)
    {
        var activity = await repository.GetByIdAsync(id, cancellationToken);
        if (activity is null)
        {
            return Result.Failure<ActivityDto>(Error.NotFound("Activity not found."));
        }

        activity.UpdateProgress(request.PlannedProgress, request.ActualProgress);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(activity));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var activity = await repository.GetByIdAsync(id, cancellationToken);
        if (activity is null)
        {
            return Result.Failure(Error.NotFound("Activity not found."));
        }

        var siblings = await repository.ListByProjectAsync(activity.ProjectId, cancellationToken);
        if (siblings.Any(a => a.ParentActivityId == id))
        {
            return Result.Failure(Error.Conflict("Delete or reassign the sub-activities first."));
        }

        // Its dependency links go with it; they would otherwise point at a missing activity.
        var links = await dependencyRepository.ListByActivityAsync(id, cancellationToken);
        await dependencyRepository.RemoveRangeAsync(links, cancellationToken);

        await repository.RemoveAsync(activity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// The parent must be an activity of the same project, must not be a milestone, must not be
    /// the activity itself or one of its own descendants (that would be a loop), and - because
    /// dependencies connect only leaf activities - must not already carry dependency links.
    /// </summary>
    private async Task<Error?> ValidateParentAsync(Guid projectId, Guid? activityId, Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            return null;
        }

        var all = await repository.ListByProjectAsync(projectId, cancellationToken);
        var byId = all.ToDictionary(a => a.Id);
        if (!byId.TryGetValue(parentId.Value, out var parent))
        {
            return Error.Validation("The parent activity must exist in the same project.");
        }

        if (parent.IsMilestone)
        {
            return Error.Validation("A milestone cannot have sub-activities.");
        }

        var visited = new HashSet<Guid>();
        for (var cursor = parent; cursor is not null && visited.Add(cursor.Id);
             cursor = cursor.ParentActivityId is { } up && byId.TryGetValue(up, out var next) ? next : null)
        {
            if (cursor.Id == activityId)
            {
                return Error.Conflict("An activity cannot be placed under itself or one of its own sub-activities.");
            }
        }

        if ((await dependencyRepository.ListByActivityAsync(parent.Id, cancellationToken)).Count > 0)
        {
            return Error.Conflict("An activity with dependencies cannot gain sub-activities; remove its dependencies first.");
        }

        return null;
    }

    public async Task<Result<ActivityDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var activity = await repository.GetByIdAsync(id, cancellationToken);
        if (activity is null)
        {
            return Result.Failure<ActivityDto>(Error.NotFound("Activity not found."));
        }

        var subject = new ApprovalSubject("WaterfallActivity", activity.Id, activity.TenantId, ScopeType: "Project", ScopeId: activity.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            activity.MarkPendingApproval();
        }
        else
        {
            activity.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("waterfall_activity.submit_for_approval", nameof(Activity), activity.Id.ToString(), $"Outcome: {outcome}", cancellationToken);
        return Result.Success(ToDto(activity));
    }

    private static ActivityDto ToDto(Activity activity) => new(
        activity.Id, activity.TenantId, activity.ProjectId, activity.ParentActivityId,
        activity.Name, activity.Description, activity.DeliverableId, activity.ResponsibleUserId, activity.ApproverUserId,
        activity.StartDate, activity.EndDate, activity.DurationDays, activity.ManHours, activity.Weight,
        activity.PlannedProgress, activity.ActualProgress, activity.ApprovalStatus, activity.IsMilestone);
}
