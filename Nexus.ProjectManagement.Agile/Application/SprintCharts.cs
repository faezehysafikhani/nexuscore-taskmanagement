using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

/// <summary>
/// Burn and velocity figures, replayed from a sprint's events. Pure: events in, numbers out. An
/// event counts from the end of the (UTC) day it happened on; anything recorded before the first
/// day is part of the opening scope.
/// </summary>
public static class SprintCharts
{
    /// <summary>A sprint longer than this is almost certainly a typo, and would make a huge series.</summary>
    public const int MaxSprintDays = 366;

    public static Result<SprintBurnDto> Burn(Sprint sprint, IReadOnlyList<SprintEvent> events, ChartMetric metric, DateOnly today)
    {
        if (sprint.StartDate is not { } start || sprint.EndDate is not { } end)
        {
            return Result.Failure<SprintBurnDto>(Error.Validation("The sprint needs a start and an end date before it can be charted."));
        }

        var length = end.DayNumber - start.DayNumber + 1;
        if (length > MaxSprintDays)
        {
            return Result.Failure<SprintBurnDto>(Error.Validation($"A sprint cannot be charted over more than {MaxSprintDays} days."));
        }

        // Cumulative scope and completion at the end of each day of the sprint.
        var scope = new int[length];
        var completed = new int[length];
        foreach (var e in events)
        {
            var day = Math.Clamp(DateOf(e).DayNumber - start.DayNumber, 0, length - 1);
            var amount = Amount(e, metric);
            switch (e.Type)
            {
                case SprintEventType.ScopeAdded: scope[day] += amount; break;
                case SprintEventType.ScopeRemoved: scope[day] -= amount; break;
                case SprintEventType.Completed: completed[day] += amount; break;
                case SprintEventType.Reopened: completed[day] -= amount; break;
            }
        }

        for (var day = 1; day < length; day++)
        {
            scope[day] += scope[day - 1];
            completed[day] += completed[day - 1];
        }

        // A finished sprint has all of its days; an unfinished one only the days up to today
        // (and at least the first, so a planned sprint still shows its opening scope).
        var dataThrough = sprint.Status == SprintStatus.Completed
            ? length - 1
            : Math.Clamp(today.DayNumber - start.DayNumber, 0, length - 1);

        var committed = scope[0];
        var points = Enumerable.Range(0, length).Select(day =>
        {
            var ideal = length == 1 ? 0m : Math.Round(committed * (decimal)(length - 1 - day) / (length - 1), 2);
            return day <= dataThrough
                ? new BurnPointDto(start.AddDays(day), scope[day], completed[day], scope[day] - completed[day], ideal)
                : new BurnPointDto(start.AddDays(day), null, null, null, ideal);
        }).ToList();

        var warnings = new List<string>();
        if (metric == ChartMetric.Points && events.Count > 0 && events.All(e => e.Points == 0))
        {
            warnings.Add("None of this sprint's tasks had story points, so every figure is zero; chart it by task count instead.");
        }

        return Result.Success(new SprintBurnDto(
            sprint.Id, sprint.Number, sprint.Name, sprint.Status, metric, start, end,
            committed, scope[dataThrough], completed[dataThrough], scope[dataThrough] - completed[dataThrough],
            points, warnings));
    }

    /// <summary>The completed sprints, newest <paramref name="lastSprints"/> of them, oldest first.</summary>
    public static VelocityDto Velocity(
        Guid projectId, IReadOnlyList<Sprint> sprints, IReadOnlyList<SprintEvent> events, ChartMetric metric, int lastSprints)
    {
        var rows = sprints
            .Where(s => s.Status == SprintStatus.Completed && s.StartDate is not null && s.EndDate is not null)
            .OrderByDescending(s => s.Number).Take(lastSprints).OrderBy(s => s.Number)
            .Select(sprint =>
            {
                var own = events.Where(e => e.SprintNumber == sprint.Number).ToList();
                var start = sprint.StartDate!.Value;

                int Sum(Func<SprintEvent, bool> filter) => own.Where(filter).Sum(e => Amount(e, metric));
                bool OnOrBeforeStart(SprintEvent e) => DateOf(e) <= start;

                var committed = Sum(e => e.Type == SprintEventType.ScopeAdded && OnOrBeforeStart(e))
                    - Sum(e => e.Type == SprintEventType.ScopeRemoved && OnOrBeforeStart(e));
                var finalScope = Sum(e => e.Type == SprintEventType.ScopeAdded) - Sum(e => e.Type == SprintEventType.ScopeRemoved);
                var done = Sum(e => e.Type == SprintEventType.Completed) - Sum(e => e.Type == SprintEventType.Reopened);

                return new SprintVelocityDto(
                    sprint.Id, sprint.Number, sprint.Name, start, sprint.EndDate!.Value, committed, finalScope, done,
                    Sum(e => e.Type == SprintEventType.CarriedOver),
                    finalScope > 0 ? Math.Round(done * 100m / finalScope, 1) : null);
            })
            .ToList();

        return new VelocityDto(
            projectId, metric, rows,
            rows.Count == 0 ? 0 : Math.Round((decimal)rows.Average(r => r.Completed), 1),
            rows.Count == 0 ? 0 : Math.Round((decimal)rows.Average(r => r.Committed), 1));
    }

    private static DateOnly DateOf(SprintEvent e) => DateOnly.FromDateTime(e.OccurredAtUtc.UtcDateTime);

    private static int Amount(SprintEvent e, ChartMetric metric) => metric == ChartMetric.Points ? e.Points : 1;
}
