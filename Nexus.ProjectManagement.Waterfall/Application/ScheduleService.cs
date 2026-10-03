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
    TimeProvider timeProvider) : IScheduleService
{
    public async Task<Result<ScheduleDto>> GetScheduleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var (calculation, _) = await LoadAndComputeAsync(projectId, cancellationToken);
        return calculation.IsFailure ? Result.Failure<ScheduleDto>(calculation.Error) : Result.Success(ToDto(calculation.Value!, applied: false));
    }

    public async Task<Result<ScheduleDto>> ApplyScheduleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var (calculation, activities) = await LoadAndComputeAsync(projectId, cancellationToken);
        if (calculation.IsFailure)
        {
            return Result.Failure<ScheduleDto>(calculation.Error);
        }

        foreach (var scheduled in calculation.Value!.Result.Activities)
        {
            activities[scheduled.Id].ApplySchedule(scheduled.Start, scheduled.Finish, scheduled.DurationDays);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(calculation.Value, applied: true));
    }

    public async Task<Result<ScheduleCalculation>> CalculateAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var (calculation, _) = await LoadAndComputeAsync(projectId, cancellationToken);
        return calculation;
    }

    /// <summary>Loads the project's plan and runs the calculator; also returns the activity
    /// entities so Apply can write to them.</summary>
    private async Task<(Result<ScheduleCalculation> Calculation, Dictionary<Guid, Activity> Activities)> LoadAndComputeAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return (Result.Failure<ScheduleCalculation>(Error.NotFound("Project not found.")), []);
        }

        var activities = await activityRepository.ListByProjectAsync(projectId, cancellationToken);
        var dependencies = await dependencyRepository.ListByProjectAsync(projectId, cancellationToken);

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

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var computed = ScheduleCalculator.Compute(inputs, links, calendar ?? AllDaysCalendar.Instance, project.StartDate, today);
        if (computed.IsFailure)
        {
            return (Result.Failure<ScheduleCalculation>(computed.Error), []);
        }

        warnings.AddRange(computed.Value!.Activities
            .Where(a => a.UsedDefaultDuration)
            .Select(a => $"'{a.Name}' has no duration or dates; one day is assumed."));

        return (Result.Success(new ScheduleCalculation(projectId, computed.Value, calendar is not null, warnings)),
            activities.ToDictionary(a => a.Id));
    }

    private static ScheduleDto ToDto(ScheduleCalculation calculation, bool applied) => new(
        calculation.ProjectId, calculation.Result.ProjectStart, calculation.Result.ProjectFinish, calculation.Result.ProjectDurationDays,
        calculation.UsesWorkCalendar, calculation.Result.PlannedProgress, calculation.Result.ActualProgress,
        calculation.Result.CriticalPath, calculation.Result.Activities.Select(ToDto).ToList(), calculation.Warnings, applied);

    private static ScheduledActivityDto ToDto(ScheduledActivity a) => new(
        a.Id, a.ParentId, a.Name, a.IsSummary, a.IsMilestone, a.Start, a.Finish, a.DurationDays,
        a.LateStart, a.LateFinish, a.TotalFloatDays, a.IsCritical, a.PlannedProgress, a.ActualProgress, a.UsedDefaultDuration);
}
