using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.CompositionTests;

public sealed class AgileChartTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static DateOnly Day(int offset) => Monday.AddDays(offset);

    private static Sprint NewSprint(int number, int days, SprintStatus status, DateOnly? start = null)
    {
        var from = start ?? Monday;
        var sprint = new Sprint(Guid.NewGuid(), Tenant, Project, number, $"Sprint {number}", null, from, from.AddDays(days - 1));
        if (status != SprintStatus.Planned)
        {
            sprint.Start();
        }

        if (status == SprintStatus.Completed)
        {
            sprint.Complete();
        }

        return sprint;
    }

    private static SprintEvent Event(Sprint sprint, SprintEventType type, int points, int dayOffset, Guid? task = null) =>
        new(Guid.NewGuid(), Tenant, Project, sprint.Number, task ?? Guid.NewGuid(), type, points,
            new DateTimeOffset(Day(dayOffset).ToDateTime(new TimeOnly(15, 30)), TimeSpan.Zero));

    private static SprintBurnDto Burn(Sprint sprint, IReadOnlyList<SprintEvent> events, ChartMetric metric = ChartMetric.Points, int today = 2)
    {
        var result = SprintCharts.Burn(sprint, events, metric, Day(today));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value!;
    }

    // ------------------------------------------------------------------- burn

    [Fact]
    public void Burn_TracksScopeCompletionAndRemaining_DayByDay_UpToToday()
    {
        var sprint = NewSprint(1, 5, SprintStatus.Active);
        var a = Guid.NewGuid();
        var events = new[]
        {
            Event(sprint, SprintEventType.ScopeAdded, 5, 0, a),       // committed on the first day
            Event(sprint, SprintEventType.ScopeAdded, 3, 1),          // added on day 1
            Event(sprint, SprintEventType.Completed, 5, 2, a),        // A done on day 2
        };

        var burn = Burn(sprint, events, today: 2);

        Assert.Equal((5, 8, 5, 3), (burn.CommittedScope, burn.CurrentScope, burn.CurrentCompleted, burn.CurrentRemaining));
        Assert.Equal(
            [(5, 0, 5), (8, 0, 8), (8, 5, 3)],
            burn.Points.Take(3).Select(p => (p.Scope!.Value, p.Completed!.Value, p.Remaining!.Value)));
        // Days that have not happened have no figures, but the ideal line is drawn for the whole sprint.
        Assert.All(burn.Points.Skip(3), p => Assert.Null(p.Scope));
        Assert.Equal([5m, 3.75m, 2.5m, 1.25m, 0m], burn.Points.Select(p => p.IdealRemaining));
        Assert.Equal(Enumerable.Range(0, 5).Select(Day), burn.Points.Select(p => p.Date));
    }

    [Fact]
    public void Burn_OfACompletedSprint_HasEveryDay_AndFinishesAtItsFinalState()
    {
        var sprint = NewSprint(1, 5, SprintStatus.Completed);
        var events = new[]
        {
            Event(sprint, SprintEventType.ScopeAdded, 5, 0), Event(sprint, SprintEventType.ScopeAdded, 3, 0),
            Event(sprint, SprintEventType.Completed, 5, 2), Event(sprint, SprintEventType.Completed, 3, 3)
        };

        var burn = Burn(sprint, events, today: 100);

        Assert.All(burn.Points, p => Assert.NotNull(p.Scope));
        Assert.Equal([0, 0, 5, 8, 8], burn.Points.Select(p => p.Completed!.Value));
        Assert.Equal([8, 8, 3, 0, 0], burn.Points.Select(p => p.Remaining!.Value));
        Assert.Equal((8, 8, 0), (burn.CommittedScope, burn.CurrentCompleted, burn.CurrentRemaining));
    }

    [Fact]
    public void Burn_FoldsEventsFromBeforeTheSprintIntoItsOpeningScope()
    {
        var sprint = NewSprint(1, 3, SprintStatus.Active);
        var events = new[] { Event(sprint, SprintEventType.ScopeAdded, 4, -5), Event(sprint, SprintEventType.ScopeAdded, 2, -1) };

        var burn = Burn(sprint, events, today: 0);

        Assert.Equal(6, burn.CommittedScope);
        Assert.Equal(6, burn.Points[0].Scope);
    }

    [Fact]
    public void Burn_ScopeShrinksWhenWorkLeaves_AndReopeningUndoesCompletion()
    {
        var sprint = NewSprint(1, 4, SprintStatus.Active);
        var kept = Guid.NewGuid();
        var dropped = Guid.NewGuid();
        var events = new[]
        {
            Event(sprint, SprintEventType.ScopeAdded, 5, 0, kept), Event(sprint, SprintEventType.ScopeAdded, 3, 0, dropped),
            Event(sprint, SprintEventType.Completed, 5, 1, kept),
            Event(sprint, SprintEventType.ScopeRemoved, 3, 1, dropped),
            Event(sprint, SprintEventType.Reopened, 5, 2, kept)
        };

        var burn = Burn(sprint, events, today: 3);

        Assert.Equal([8, 5, 5, 5], burn.Points.Select(p => p.Scope!.Value));
        Assert.Equal([0, 5, 0, 0], burn.Points.Select(p => p.Completed!.Value));
    }

    [Fact]
    public void Burn_ByCount_IgnoresPoints_AndAReEstimateIsNotAScopeChange()
    {
        var sprint = NewSprint(1, 3, SprintStatus.Active);
        var task = Guid.NewGuid();
        var events = new[]
        {
            Event(sprint, SprintEventType.ScopeAdded, 3, 0, task), Event(sprint, SprintEventType.ScopeAdded, 8, 0),
            // Re-estimated 3 -> 5: removed at the old size, added at the new.
            Event(sprint, SprintEventType.ScopeRemoved, 3, 1, task), Event(sprint, SprintEventType.ScopeAdded, 5, 1, task)
        };

        var byCount = Burn(sprint, events, ChartMetric.Count, today: 2);
        var byPoints = Burn(sprint, events, ChartMetric.Points, today: 2);

        Assert.Equal([2, 2, 2], byCount.Points.Select(p => p.Scope!.Value)); // two tasks throughout
        Assert.Equal([11, 13, 13], byPoints.Points.Select(p => p.Scope!.Value));
    }

    [Fact]
    public void Burn_ForAPlannedSprint_ShowsItsOpeningScope_AndNothingMore()
    {
        var sprint = NewSprint(1, 5, SprintStatus.Planned);

        var burn = Burn(sprint, [Event(sprint, SprintEventType.ScopeAdded, 4, -2)], today: -10);

        Assert.Equal(4, burn.Points[0].Scope);
        Assert.All(burn.Points.Skip(1), p => Assert.Null(p.Scope));
    }

    [Fact]
    public void Burn_OfAOneDaySprint_HasAZeroIdealLine()
    {
        var sprint = NewSprint(1, 1, SprintStatus.Active);

        var burn = Burn(sprint, [Event(sprint, SprintEventType.ScopeAdded, 4, 0)], today: 0);

        Assert.Equal([0m], burn.Points.Select(p => p.IdealRemaining));
    }

    [Fact]
    public void Burn_WarnsWhenNoTaskHadPoints_AndNotWhenSomeDid()
    {
        var sprint = NewSprint(1, 3, SprintStatus.Active);

        var unsized = Burn(sprint, [Event(sprint, SprintEventType.ScopeAdded, 0, 0)]);
        Assert.Contains("task count", Assert.Single(unsized.Warnings));
        Assert.Empty(Burn(sprint, [Event(sprint, SprintEventType.ScopeAdded, 0, 0)], ChartMetric.Count).Warnings);
        Assert.Empty(Burn(sprint, [Event(sprint, SprintEventType.ScopeAdded, 2, 0)]).Warnings);
        Assert.Empty(Burn(sprint, []).Warnings);
    }

    [Fact]
    public void Burn_NeedsDates_AndRefusesAnAbsurdRange()
    {
        var undated = new Sprint(Guid.NewGuid(), Tenant, Project, 1, "S", null, null, null);
        Assert.Equal("validation.error", SprintCharts.Burn(undated, [], ChartMetric.Points, Monday).Error.Code);

        var huge = NewSprint(1, SprintCharts.MaxSprintDays + 1, SprintStatus.Active);
        Assert.Equal("validation.error", SprintCharts.Burn(huge, [], ChartMetric.Points, Monday).Error.Code);
        Assert.True(SprintCharts.Burn(NewSprint(1, SprintCharts.MaxSprintDays, SprintStatus.Active), [], ChartMetric.Points, Monday).IsSuccess);
    }

    [Fact]
    public void Burn_AssignsAnEventToItsUtcDay()
    {
        var sprint = NewSprint(1, 3, SprintStatus.Completed);
        // 23:30 UTC on day 0 is still day 0; 00:30 UTC on day 1 is day 1.
        var late = new SprintEvent(Guid.NewGuid(), Tenant, Project, 1, Guid.NewGuid(), SprintEventType.ScopeAdded, 1,
            new DateTimeOffset(Day(0).ToDateTime(new TimeOnly(23, 30)), TimeSpan.Zero));
        var early = new SprintEvent(Guid.NewGuid(), Tenant, Project, 1, Guid.NewGuid(), SprintEventType.ScopeAdded, 10,
            new DateTimeOffset(Day(1).ToDateTime(new TimeOnly(0, 30)), TimeSpan.Zero));

        var burn = Burn(sprint, [late, early]);

        Assert.Equal([1, 11, 11], burn.Points.Select(p => p.Scope!.Value));
    }

    // --------------------------------------------------------------- velocity

    [Fact]
    public void Velocity_ComparesWhatWasCommittedWithWhatWasCompleted_PerCompletedSprint()
    {
        var one = NewSprint(1, 14, SprintStatus.Completed);
        var two = NewSprint(2, 14, SprintStatus.Completed, Day(14));
        var active = NewSprint(3, 14, SprintStatus.Active, Day(28));
        var events = new[]
        {
            // Sprint 1: committed 8, one 5-point task added on day 3, 10 completed, 3 carried over.
            Event(one, SprintEventType.ScopeAdded, 5, 0), Event(one, SprintEventType.ScopeAdded, 3, 0), Event(one, SprintEventType.ScopeAdded, 5, 3),
            Event(one, SprintEventType.Completed, 5, 6), Event(one, SprintEventType.Completed, 5, 9),
            Event(one, SprintEventType.CarriedOver, 3, 13),
            // Sprint 2: committed 10, 8 completed.
            Event(two, SprintEventType.ScopeAdded, 10, 14), Event(two, SprintEventType.Completed, 8, 20),
            // The running sprint never appears.
            Event(active, SprintEventType.ScopeAdded, 20, 28)
        };

        var velocity = SprintCharts.Velocity(Project, [one, two, active], events, ChartMetric.Points, 5);

        Assert.Equal([1, 2], velocity.Sprints.Select(s => s.Number));
        var first = velocity.Sprints[0];
        Assert.Equal((8, 13, 10, 3, 76.9m), (first.Committed, first.FinalScope, first.Completed, first.CarriedOver, first.CompletionPercent));
        var second = velocity.Sprints[1];
        Assert.Equal((10, 10, 8, 0, 80m), (second.Committed, second.FinalScope, second.Completed, second.CarriedOver, second.CompletionPercent));
        Assert.Equal((9m, 9m), (velocity.AverageVelocity, velocity.AverageCommitted));
    }

    [Fact]
    public void Velocity_UsesOnlyTheMostRecentSprintsRequested_OldestFirst()
    {
        var sprints = Enumerable.Range(1, 6).Select(n => NewSprint(n, 7, SprintStatus.Completed, Day(n * 7))).ToList();
        var events = sprints.SelectMany(s => new[]
        {
            Event(s, SprintEventType.ScopeAdded, 10, (s.Number * 7)), Event(s, SprintEventType.Completed, s.Number * 2, s.Number * 7 + 3)
        }).ToList();

        var velocity = SprintCharts.Velocity(Project, sprints, events, ChartMetric.Points, 3);

        Assert.Equal([4, 5, 6], velocity.Sprints.Select(s => s.Number));
        Assert.Equal([8, 10, 12], velocity.Sprints.Select(s => s.Completed));
        Assert.Equal(10m, velocity.AverageVelocity);
    }

    [Fact]
    public void Velocity_ByCount_CountsTasks_AndAReEstimateDoesNotChangeIt()
    {
        var sprint = NewSprint(1, 7, SprintStatus.Completed);
        var task = Guid.NewGuid();
        var events = new[]
        {
            Event(sprint, SprintEventType.ScopeAdded, 3, 0, task), Event(sprint, SprintEventType.ScopeRemoved, 3, 2, task), Event(sprint, SprintEventType.ScopeAdded, 8, 2, task),
            Event(sprint, SprintEventType.Completed, 8, 4, task)
        };

        var velocity = SprintCharts.Velocity(Project, [sprint], events, ChartMetric.Count, 5);

        var row = Assert.Single(velocity.Sprints);
        Assert.Equal((1, 1, 1), (row.Committed, row.FinalScope, row.Completed));
        Assert.Equal(100m, row.CompletionPercent);
    }

    [Fact]
    public void Velocity_ForAProjectWithNoCompletedSprints_IsEmptyAndZero()
    {
        var velocity = SprintCharts.Velocity(Project, [NewSprint(1, 7, SprintStatus.Active)], [], ChartMetric.Points, 5);

        Assert.Empty(velocity.Sprints);
        Assert.Equal((0m, 0m), (velocity.AverageVelocity, velocity.AverageCommitted));
    }

    [Fact]
    public void Velocity_OfASprintWithNothingInIt_HasNoCompletionPercent()
    {
        var velocity = SprintCharts.Velocity(Project, [NewSprint(1, 7, SprintStatus.Completed)], [], ChartMetric.Points, 5);

        Assert.Null(velocity.Sprints.Single().CompletionPercent);
    }

    // ------------------------------------------------------------- end to end

    [Fact]
    public async Task EndToEnd_AWholeSprintLifecycle_ProducesTheExpectedBurnAndVelocity()
    {
        var f = new AgileFixture();
        var charts = new SprintChartService(f.Sprints, f.Events, f.Clock);

        f.Clock.Set(Day(0));
        var sprint = await f.AddSprintAsync(Day(0), Day(4));
        var a = await f.AddTaskAsync("A", points: 5);
        var b = await f.AddTaskAsync("B", points: 3);
        var c = await f.AddTaskAsync("C", points: 2);
        await f.SprintService.AssignTasksAsync(sprint.Id, new AssignSprintTasksRequest([a.Id, b.Id]), default);
        await f.SprintService.StartAsync(sprint.Id, new StartSprintRequest(null, null), default);

        f.Clock.Set(Day(1));
        await f.SprintService.AssignTasksAsync(sprint.Id, new AssignSprintTasksRequest([c.Id]), default); // scope creep
        f.Clock.Set(Day(2));
        await f.TaskService.ChangeStatusAsync(a.Id, new ChangeAgileTaskStatusRequest(AgileTaskStatus.Done), default);
        f.Clock.Set(Day(3));
        await f.TaskService.ChangeStatusAsync(c.Id, new ChangeAgileTaskStatusRequest(AgileTaskStatus.Done), default);

        f.Clock.Set(Day(4));
        var running = (await charts.GetBurnAsync(sprint.Id, ChartMetric.Points, default)).Value!;
        Assert.Equal([8, 10, 10, 10, 10], running.Points.Select(p => p.Scope!.Value));
        Assert.Equal([0, 0, 5, 7, 7], running.Points.Select(p => p.Completed!.Value));
        Assert.Equal(8, running.CommittedScope);

        await f.SprintService.CompleteAsync(sprint.Id, new CompleteSprintRequest(null), default);

        var final = (await charts.GetBurnAsync(sprint.Id, ChartMetric.Points, default)).Value!;
        Assert.Equal(running.Points.Select(p => (p.Scope, p.Completed)), final.Points.Select(p => (p.Scope, p.Completed))); // carry-over did not rewrite it
        Assert.Equal(SprintStatus.Completed, final.Status);

        var velocity = (await charts.GetVelocityAsync(f.ProjectId, 5, ChartMetric.Points, default)).Value!;
        var row = Assert.Single(velocity.Sprints);
        Assert.Equal((8, 10, 7, 3), (row.Committed, row.FinalScope, row.Completed, row.CarriedOver)); // B (3 points) carried over
    }

    [Fact]
    public async Task TheChartService_ReportsUnknownSprints_AndOutOfRangeVelocityWindows()
    {
        var f = new AgileFixture();
        var charts = new SprintChartService(f.Sprints, f.Events, f.Clock);

        Assert.Equal("not_found", (await charts.GetBurnAsync(Guid.NewGuid(), ChartMetric.Points, default)).Error.Code);
        Assert.Equal("validation.error", (await charts.GetVelocityAsync(f.ProjectId, 0, ChartMetric.Points, default)).Error.Code);
        Assert.Equal("validation.error", (await charts.GetVelocityAsync(f.ProjectId, SprintChartService.MaxVelocitySprints + 1, ChartMetric.Points, default)).Error.Code);

        var undated = await f.AddSprintAsync();
        Assert.Equal("validation.error", (await charts.GetBurnAsync(undated.Id, ChartMetric.Points, default)).Error.Code);
    }
}
