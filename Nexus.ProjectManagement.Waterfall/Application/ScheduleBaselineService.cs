using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public sealed class ScheduleBaselineService(
    IScheduleBaselineRepository repository,
    IScheduleService scheduleService,
    IWaterfallUnitOfWork unitOfWork) : IScheduleBaselineService
{
    /// <summary>A project's baselines are for a handful of agreed plans, not a change log.</summary>
    public const int MaxBaselinesPerProject = 50;

    public async Task<Result<IReadOnlyList<ScheduleBaselineDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var baselines = await repository.ListByProjectAsync(projectId, cancellationToken);
        var activities = await repository.ListActivitiesAsync(baselines.Select(b => b.Id).ToList(), cancellationToken);
        var counts = activities.GroupBy(a => a.BaselineId).ToDictionary(g => g.Key, g => g.Count());

        return Result.Success<IReadOnlyList<ScheduleBaselineDto>>(
            baselines.OrderBy(b => b.Number).Select(b => ToDto(b, counts.GetValueOrDefault(b.Id))).ToList());
    }

    public async Task<Result<ScheduleBaselineDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var baseline = await repository.GetByIdAsync(id, cancellationToken);
        if (baseline is null)
        {
            return Result.Failure<ScheduleBaselineDetailDto>(Error.NotFound("Baseline not found."));
        }

        var activities = await repository.ListActivitiesAsync([id], cancellationToken);
        return Result.Success(ToDetail(baseline, activities));
    }

    public async Task<Result<ScheduleBaselineDetailDto>> CreateAsync(CreateScheduleBaselineRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<ScheduleBaselineDetailDto>(Error.Validation("Name is required."));
        }

        var existing = await repository.ListByProjectAsync(request.ProjectId, cancellationToken);
        if (existing.Count >= MaxBaselinesPerProject)
        {
            return Result.Failure<ScheduleBaselineDetailDto>(Error.Conflict($"A project can hold at most {MaxBaselinesPerProject} baselines; delete one first."));
        }

        var schedule = await scheduleService.GetScheduleAsync(request.ProjectId, cancellationToken);
        if (schedule.IsFailure)
        {
            return Result.Failure<ScheduleBaselineDetailDto>(schedule.Error);
        }

        if (schedule.Value!.Activities.Count == 0)
        {
            return Result.Failure<ScheduleBaselineDetailDto>(Error.Validation("A project with no activities has nothing to baseline."));
        }

        // Highest + 1: deleting baseline 2 of 3 leaves 1 and 3 and the next is 4, so "baseline 2"
        // never silently becomes a different plan. (Deleting the latest one does free its number.)
        var number = existing.Count == 0 ? 1 : existing.Max(b => b.Number) + 1;
        var baseline = new ScheduleBaseline(
            Guid.NewGuid(), request.TenantId, request.ProjectId, number, request.Name, request.Note,
            schedule.Value.ProjectStart, schedule.Value.ProjectFinish);
        var activities = schedule.Value.Activities
            .Select(a => new ScheduleBaselineActivity(
                Guid.NewGuid(), baseline.Id, a.Id, a.ParentActivityId, a.Name, a.IsSummary, a.IsMilestone,
                a.Start, a.Finish, a.DurationDays))
            .ToList();

        await repository.AddAsync(baseline, activities, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDetail(baseline, activities));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var baseline = await repository.GetByIdAsync(id, cancellationToken);
        if (baseline is null)
        {
            return Result.Failure(Error.NotFound("Baseline not found."));
        }

        var activities = await repository.ListActivitiesAsync([id], cancellationToken);
        await repository.RemoveAsync(baseline, activities.ToList(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<BaselineVarianceDto>> GetVarianceAsync(Guid id, CancellationToken cancellationToken)
    {
        var baseline = await repository.GetByIdAsync(id, cancellationToken);
        if (baseline is null)
        {
            return Result.Failure<BaselineVarianceDto>(Error.NotFound("Baseline not found."));
        }

        var current = await scheduleService.GetScheduleAsync(baseline.ProjectId, cancellationToken);
        if (current.IsFailure)
        {
            return Result.Failure<BaselineVarianceDto>(current.Error);
        }

        var baselineActivities = await repository.ListActivitiesAsync([id], cancellationToken);
        var variances = BuildVariances(baselineActivities, current.Value!.Activities);

        return Result.Success(new BaselineVarianceDto(
            baseline.Id, baseline.Number, baseline.Name, baseline.ProjectId,
            baseline.ProjectStart, baseline.ProjectFinish, current.Value.ProjectStart, current.Value.ProjectFinish,
            Days(baseline.ProjectStart, current.Value.ProjectStart), Days(baseline.ProjectFinish, current.Value.ProjectFinish),
            variances.Count(v => v.Status == VarianceStatus.OnTrack), variances.Count(v => v.Status == VarianceStatus.Late),
            variances.Count(v => v.Status == VarianceStatus.Early), variances.Count(v => v.Status == VarianceStatus.Added),
            variances.Count(v => v.Status == VarianceStatus.Removed), variances));
    }

    private static List<ActivityVarianceDto> BuildVariances(
        IReadOnlyList<ScheduleBaselineActivity> baseline, IReadOnlyList<ScheduledActivityDto> current)
    {
        var baselineById = baseline.ToDictionary(a => a.ActivityId);
        var currentById = current.ToDictionary(a => a.Id);
        var rows = new List<ActivityVarianceDto>();

        foreach (var now in current)
        {
            if (!baselineById.TryGetValue(now.Id, out var then))
            {
                rows.Add(new ActivityVarianceDto(
                    now.Id, now.Name, now.IsSummary, now.IsMilestone, VarianceStatus.Added,
                    null, null, null, now.Start, now.Finish, now.DurationDays, null, null, null));
                continue;
            }

            var finishVariance = Days(then.EndDate, now.Finish);
            rows.Add(new ActivityVarianceDto(
                now.Id, now.Name, now.IsSummary, now.IsMilestone,
                finishVariance > 0 ? VarianceStatus.Late : finishVariance < 0 ? VarianceStatus.Early : VarianceStatus.OnTrack,
                then.StartDate, then.EndDate, then.DurationDays, now.Start, now.Finish, now.DurationDays,
                Days(then.StartDate, now.Start), finishVariance, now.DurationDays - then.DurationDays));
        }

        rows.AddRange(baseline.Where(then => !currentById.ContainsKey(then.ActivityId)).Select(then => new ActivityVarianceDto(
            then.ActivityId, then.Name, then.IsSummary, then.IsMilestone, VarianceStatus.Removed,
            then.StartDate, then.EndDate, then.DurationDays, null, null, null, null, null, null)));

        return rows.OrderBy(r => r.CurrentStart ?? r.BaselineStart).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
    }

    private static int Days(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber;

    private static ScheduleBaselineDto ToDto(ScheduleBaseline baseline, int activityCount) => new(
        baseline.Id, baseline.TenantId, baseline.ProjectId, baseline.Number, baseline.Name, baseline.Note,
        baseline.ProjectStart, baseline.ProjectFinish, activityCount, baseline.CreatedAtUtc, baseline.CreatedByUserId);

    private static ScheduleBaselineDetailDto ToDetail(ScheduleBaseline baseline, IReadOnlyCollection<ScheduleBaselineActivity> activities) => new(
        ToDto(baseline, activities.Count),
        activities.OrderBy(a => a.StartDate).ThenBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => new BaselineActivityDto(a.ActivityId, a.ParentActivityId, a.Name, a.IsSummary, a.IsMilestone, a.StartDate, a.EndDate, a.DurationDays))
            .ToList());
}
