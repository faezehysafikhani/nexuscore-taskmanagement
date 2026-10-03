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
        var (dto, _) = await CalculateAsync(projectId, applied: false, cancellationToken);
        return dto;
    }

    public async Task<Result<ScheduleDto>> ApplyScheduleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var (dto, activities) = await CalculateAsync(projectId, applied: true, cancellationToken);
        if (dto.IsFailure)
        {
            return dto;
        }

        foreach (var scheduled in dto.Value!.Activities)
        {
            activities[scheduled.Id].ApplySchedule(scheduled.Start, scheduled.Finish, scheduled.DurationDays);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return dto;
    }

    /// <summary>Loads the project's plan and runs the calculator; also returns the activity
    /// entities so Apply can write to them.</summary>
    internal async Task<(Result<ScheduleDto> Schedule, Dictionary<Guid, Activity> Activities)> CalculateAsync(
        Guid projectId, bool applied, CancellationToken cancellationToken)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return (Result.Failure<ScheduleDto>(Error.NotFound("Project not found.")), []);
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
            return (Result.Failure<ScheduleDto>(computed.Error), []);
        }

        var result = computed.Value!;
        warnings.AddRange(result.Activities
            .Where(a => a.UsedDefaultDuration)
            .Select(a => $"'{a.Name}' has no duration or dates; one day is assumed."));

        var dto = new ScheduleDto(
            projectId, result.ProjectStart, result.ProjectFinish, result.ProjectDurationDays,
            UsesWorkCalendar: calendar is not null, result.PlannedProgress, result.ActualProgress,
            result.CriticalPath, result.Activities.Select(ToDto).ToList(), warnings, applied);

        return (Result.Success(dto), activities.ToDictionary(a => a.Id));
    }

    private static ScheduledActivityDto ToDto(ScheduledActivity a) => new(
        a.Id, a.ParentId, a.Name, a.IsSummary, a.IsMilestone, a.Start, a.Finish, a.DurationDays,
        a.LateStart, a.LateFinish, a.TotalFloatDays, a.IsCritical, a.PlannedProgress, a.ActualProgress, a.UsedDefaultDuration);
}
