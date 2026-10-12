using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public sealed class ScheduleService(
    IActivityRepository activityRepository,
    IActivityDependencyRepository dependencyRepository,
    IProjectRepository projectRepository,
    IWorkingDayCalendarProvider calendarProvider,
    IWaterfallUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IWaterfallSettingsRepository? settingsRepository = null,
    WaterfallOptions? options = null) : IScheduleService
{
    public async Task<Result<ScheduleDto>> GetScheduleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var loaded = await LoadAndComputeAsync(projectId, cancellationToken);
        return loaded.Calculation.IsFailure
            ? Result.Failure<ScheduleDto>(loaded.Calculation.Error)
            : Result.Success(ToDto(loaded, applied: false));
    }

    public async Task<Result<ProjectPlanDto>> GetPlanAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var loaded = await LoadAndComputeAsync(projectId, cancellationToken);
        if (loaded.Calculation.IsFailure)
        {
            return Result.Failure<ProjectPlanDto>(loaded.Calculation.Error);
        }

        return Result.Success(new ProjectPlanDto(
            projectId,
            WaterfallSettingsService.ToDto(projectId, loaded.Stored, options),
            loaded.Activities.Values.OrderBy(a => a.Position).ThenBy(a => a.CreatedAtUtc).ThenBy(a => a.Name, StringComparer.Ordinal).Select(ToDto).ToList(),
            loaded.Dependencies.Select(ToDto).ToList(),
            ToDto(loaded, applied: false)));
    }

    public async Task<Result<IReadOnlyList<ProjectPlanDto>>> GetPlansAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken)
    {
        var plans = new List<ProjectPlanDto>();
        foreach (var projectId in projectIds.Distinct())
        {
            var plan = await GetPlanAsync(projectId, cancellationToken);
            if (plan.IsSuccess)
            {
                plans.Add(plan.Value!);
            }
            else if (plan.Error.Code != "not_found")
            {
                // A project whose plan cannot be calculated (a circular dependency in stored data) must not hide everybody else's.
                continue;
            }
        }

        return Result.Success<IReadOnlyList<ProjectPlanDto>>(plans);
    }

    public async Task<Result<ScheduleDto>> ApplyScheduleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var loaded = await LoadAndComputeAsync(projectId, cancellationToken);
        if (loaded.Calculation.IsFailure)
        {
            return Result.Failure<ScheduleDto>(loaded.Calculation.Error);
        }

        foreach (var scheduled in loaded.Calculation.Value!.Result.Activities)
        {
            loaded.Activities[scheduled.Id].ApplySchedule(scheduled.Start, scheduled.Finish, scheduled.DurationDays);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(loaded, applied: true));
    }

    public async Task<Result<ScheduleCalculation>> CalculateAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var loaded = await LoadAndComputeAsync(projectId, cancellationToken);
        return loaded.Calculation;
    }

    private sealed record Loaded(
        Result<ScheduleCalculation> Calculation, Dictionary<Guid, Activity> Activities, IReadOnlyList<ActivityDependency> Dependencies,
        WaterfallProjectSettings? Stored, WaterfallSettingsDto Settings);

    /// <summary>Loads the project's plan and runs the calculator; also returns the activity
    /// entities so Apply can write to them.</summary>
    private async Task<Loaded> LoadAndComputeAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return new Loaded(Result.Failure<ScheduleCalculation>(Error.NotFound("Project not found.")), [], [], null,
                WaterfallSettingsService.ToDto(projectId, null, options));
        }

        var activities = await activityRepository.ListByProjectAsync(projectId, cancellationToken);
        var dependencies = await dependencyRepository.ListByProjectAsync(projectId, cancellationToken);
        var stored = settingsRepository is null ? null : await settingsRepository.GetAsync(projectId, cancellationToken);
        var settings = WaterfallSettingsService.ToDto(projectId, stored, options);
        var byId = activities.ToDictionary(a => a.Id);

        var warnings = new List<string>();
        var calendar = await calendarProvider.GetAsync(project.TenantId, project.WorkCalendarId, cancellationToken);
        if (calendar is null && project.WorkCalendarId is not null)
        {
            warnings.Add("The project's work calendar could not be loaded; every day is counted as a working day.");
        }

        var inputs = activities.Select(a => new ScheduleActivityInput(
            a.Id, a.ParentActivityId, a.Name, a.IsMilestone, a.DurationDays, a.StartDate, a.EndDate,
            a.Weight, a.PlannedProgress, a.ActualProgress)).ToList();
        var links = dependencies.Select(d => new ScheduleLinkInput(d.PredecessorActivityId, d.SuccessorActivityId, d.Type, d.LagDays)).ToList();
        var effectiveCalendar = calendar ?? AllDaysCalendar.Instance;

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var computed = ScheduleCalculator.Compute(inputs, links, effectiveCalendar, project.StartDate, today);
        if (computed.IsFailure)
        {
            return new Loaded(Result.Failure<ScheduleCalculation>(computed.Error), [], dependencies, stored, settings);
        }

        // How a project weighs its activities (and whether groups count for what is inside them) is decided here, from the durations
        // the first pass worked out; the second pass then rolls progress up with those weights.
        var rollUp = options?.RollUpSummaryWeights == true || settings.Weighting != WeightingMode.Manual;
        if (rollUp)
        {
            inputs = Reweigh(inputs, computed.Value!, byId, settings);
            computed = ScheduleCalculator.Compute(inputs, links, effectiveCalendar, project.StartDate, today);
            if (computed.IsFailure)
            {
                return new Loaded(Result.Failure<ScheduleCalculation>(computed.Error), [], dependencies, stored, settings);
            }
        }

        var result = computed.Value!;
        if (settings.PlannedProgress != PlannedProgressMode.Stored)
        {
            var curve = new PlannedProgressCurve(result, settings.PlannedProgress);
            var planned = curve.AllAt(today);
            result = result with
            {
                Activities = result.Activities.Select(a => a with { PlannedProgress = Math.Round(planned[a.Id], 2) }).ToList(),
                PlannedProgress = Math.Round(curve.At(today), 2)
            };
        }

        warnings.AddRange(result.Activities
            .Where(a => a.UsedDefaultDuration)
            .Select(a => $"'{a.Name}' has no duration or dates; one day is assumed."));

        return new Loaded(
            Result.Success(new ScheduleCalculation(projectId, result, calendar is not null, warnings, effectiveCalendar, settings.PlannedProgress)),
            byId, dependencies, stored, settings);
    }

    /// <summary>Gives every leaf its effective weight and every group the sum of its children's.</summary>
    private static List<ScheduleActivityInput> Reweigh(
        List<ScheduleActivityInput> inputs, ScheduleResult firstPass, Dictionary<Guid, Activity> byId, WaterfallSettingsDto settings)
    {
        var scheduled = firstPass.Activities.ToDictionary(a => a.Id);
        var leaves = firstPass.Activities.Where(a => !a.IsSummary).ToList();

        var weights = new Dictionary<Guid, decimal>();
        if (settings.Weighting == WeightingMode.Manual)
        {
            foreach (var leaf in leaves)
            {
                weights[leaf.Id] = byId[leaf.Id].Weight;
            }
        }
        else
        {
            var computed = ActivityWeighting.Compute(
                leaves.Select(l => new WeightingInput(l.Id, l.DurationDays, byId[l.Id].Cost ?? 0, byId[l.Id].Complexity)).ToList(),
                settings.Weighting, settings.TimeFactor, settings.CostFactor, settings.ComplexityFactor);
            foreach (var pair in computed)
            {
                weights[pair.Key] = pair.Value;
            }
        }

        var children = firstPass.Activities.Where(a => a.ParentId is not null).GroupBy(a => a.ParentId!.Value, a => a.Id).ToDictionary(g => g.Key, g => g.ToList());
        decimal WeightOf(Guid id)
        {
            if (weights.TryGetValue(id, out var known))
            {
                return known;
            }

            return weights[id] = children.TryGetValue(id, out var kids) ? kids.Sum(WeightOf) : 0m;
        }

        return inputs.Select(i => i with { Weight = WeightOf(i.Id) }).ToList();
    }

    private static ScheduleDto ToDto(Loaded loaded, bool applied)
    {
        var calculation = loaded.Calculation.Value!;
        var result = calculation.Result;
        var scheduled = result.Activities.ToDictionary(a => a.Id);

        // WBS codes follow the order activities were placed in (Position, then creation), not the order they start in.
        var childrenOf = result.Activities.Where(a => a.ParentId is not null)
            .GroupBy(a => a.ParentId!.Value, a => a.Id).ToDictionary(g => g.Key, g => g.ToList());
        int Sequence(Guid id) => loaded.Activities.TryGetValue(id, out var a) ? a.Position : 0;
        IEnumerable<Guid> Ordered(IEnumerable<Guid> ids) =>
            ids.OrderBy(Sequence)
               .ThenBy(id => loaded.Activities.TryGetValue(id, out var a) ? a.CreatedAtUtc : default)
               .ThenBy(id => scheduled[id].Name, StringComparer.Ordinal);

        var wbs = new Dictionary<Guid, (string Code, int Level)>();
        void Number(IEnumerable<Guid> ids, string prefix, int level)
        {
            var index = 1;
            foreach (var id in Ordered(ids))
            {
                var code = prefix.Length == 0 ? $"{index}" : $"{prefix}.{index}";
                wbs[id] = (code, level);
                if (childrenOf.TryGetValue(id, out var kids))
                {
                    Number(kids, code, level + 1);
                }

                index++;
            }
        }

        Number(result.Activities.Where(a => a.ParentId is null).Select(a => a.Id), "", 1);

        var rolled = new Dictionary<Guid, (decimal Weight, decimal Cost, decimal ManHours)>();
        (decimal Weight, decimal Cost, decimal ManHours) Roll(Guid id)
        {
            if (rolled.TryGetValue(id, out var known))
            {
                return known;
            }

            if (childrenOf.TryGetValue(id, out var kids))
            {
                var parts = kids.Select(Roll).ToList();
                return rolled[id] = (parts.Sum(p => p.Weight), parts.Sum(p => p.Cost), parts.Sum(p => p.ManHours));
            }

            var entity = loaded.Activities.GetValueOrDefault(id);
            return rolled[id] = (scheduled[id].Weight, entity?.Cost ?? 0m, entity?.ManHours ?? 0m);
        }

        ScheduledActivityDto Map(ScheduledActivity a)
        {
            var (weight, cost, manHours) = Roll(a.Id);
            var (code, level) = wbs.GetValueOrDefault(a.Id, ("", 1));
            return new ScheduledActivityDto(
                a.Id, a.ParentId, a.Name, a.IsSummary, a.IsMilestone, a.Start, a.Finish, a.DurationDays,
                a.LateStart, a.LateFinish, a.TotalFloatDays, a.IsCritical, a.PlannedProgress, a.ActualProgress, a.UsedDefaultDuration,
                code, level, weight, cost, manHours);
        }

        return new ScheduleDto(
            calculation.ProjectId, result.ProjectStart, result.ProjectFinish, result.ProjectDurationDays,
            calculation.UsesWorkCalendar, result.PlannedProgress, result.ActualProgress,
            result.CriticalPath, result.Activities.Select(Map).ToList(), calculation.Warnings, applied,
            loaded.Settings.Weighting, loaded.Settings.PlannedProgress);
    }

    private static ActivityDto ToDto(Activity activity) => ActivityService.ToDto(activity);

    private static ActivityDependencyDto ToDto(ActivityDependency dependency) => new(
        dependency.Id, dependency.TenantId, dependency.ProjectId, dependency.PredecessorActivityId,
        dependency.SuccessorActivityId, dependency.Type, dependency.LagDays);
}
