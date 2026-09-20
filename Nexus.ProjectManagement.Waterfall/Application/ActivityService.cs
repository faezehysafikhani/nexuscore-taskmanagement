using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.Application.Approvals;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public sealed class ActivityService(
    IActivityRepository repository,
    IWaterfallUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester,
    IPlatformService platformService) : IActivityService
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

        var activity = new Activity(Guid.NewGuid(), request.TenantId, request.ProjectId, request.Name, request.ParentActivityId);
        activity.UpdateDetails(
            request.Name, request.Description, request.ParentActivityId, request.DeliverableId,
            request.ResponsibleUserId, request.ApproverUserId,
            request.StartDate, request.EndDate, request.DurationDays, request.ManHours, request.Weight);

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

        activity.UpdateDetails(
            request.Name, request.Description, request.ParentActivityId, request.DeliverableId,
            request.ResponsibleUserId, request.ApproverUserId,
            request.StartDate, request.EndDate, request.DurationDays, request.ManHours, request.Weight);

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

        await repository.RemoveAsync(activity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
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
        activity.PlannedProgress, activity.ActualProgress, activity.ApprovalStatus);
}
