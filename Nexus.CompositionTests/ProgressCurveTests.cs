using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.CompositionTests;

public sealed class ProgressCurveTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly Today = Monday.AddDays(2); // Wednesday

    private static DateOnly Day(int offset) => Monday.AddDays(offset);

    // ---------------------------------------------------- the planned curve

    private static ScheduleResult Calculate(
        IReadOnlyList<ScheduleActivityInput> activities, IReadOnlyList<ScheduleLinkInput> links, IWorkingDayCalendar? calendar = null)
    {
        var result = ScheduleCalculator.Compute(activities, links, calendar ?? AllDaysCalendar.Instance, Monday, Monday);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    private static ScheduleActivityInput Task(Guid id, string name, int days, decimal weight = 0, Guid? parent = null) =>
        new(id, parent, name, false, days, null, null, weight, 0, 0);

    private sealed class Weekdays : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    [Fact]
    public void ASingleTask_CompletesEvenlyAcrossItsDays_AndIsFullyDoneAfterIt()
    {
        var curve = new PlannedProgressCurve(Calculate([Task(Guid.NewGuid(), "A", 4)], []));

        Assert.Equal(0m, curve.At(Day(-1)));
        Assert.Equal(25m, curve.At(Day(0)));
        Assert.Equal(50m, curve.At(Day(1)));
        Assert.Equal(75m, curve.At(Day(2)));
        Assert.Equal(100m, curve.At(Day(3)));
        Assert.Equal(100m, curve.At(Day(30)));
    }

    [Fact]
    public void WeekendsAddNothing_TheCurveFlatLinesOverThem()
    {
        var curve = new PlannedProgressCurve(Calculate([Task(Guid.NewGuid(), "A", 5), Task(Guid.NewGuid(), "B", 5)], [], new Weekdays()));
        // Two parallel 5-day tasks, Mon-Fri: 20% a day, then nothing over the weekend.

        Assert.Equal(20m, curve.At(Day(0)));
        Assert.Equal(100m, curve.At(Day(4)));
        Assert.Equal(100m, curve.At(Day(5))); // Saturday
        Assert.Equal(100m, curve.At(Day(6))); // Sunday
    }

    [Fact]
    public void ASaturdayInTheMiddleOfAPlan_HoldsTheFridaysValue()
    {
        var a = Guid.NewGuid();
        var activities = new[] { Task(a, "A", 10) };

        var curve = new PlannedProgressCurve(Calculate(activities, [], new Weekdays()));

        Assert.Equal(50m, curve.At(Day(4))); // Friday: 5 of 10 working days
        Assert.Equal(50m, curve.At(Day(5))); // Saturday: nothing more happens
        Assert.Equal(50m, curve.At(Day(6)));
        Assert.Equal(60m, curve.At(Day(7))); // Monday
    }

    [Fact]
    public void SequentialTasks_AreCombinedByTheirWeights()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var curve = new PlannedProgressCurve(Calculate(
            [Task(a, "A", 2, weight: 75), Task(b, "B", 2, weight: 25)],
            [new ScheduleLinkInput(a, b, DependencyType.FinishToStart, 0)]));

        Assert.Equal(37.5m, curve.At(Day(0)));  // half of A (75%) = 37.5
        Assert.Equal(75m, curve.At(Day(1)));    // A done
        Assert.Equal(87.5m, curve.At(Day(2)));  // plus half of B (25%)
        Assert.Equal(100m, curve.At(Day(3)));
    }

    [Fact]
    public void WithoutWeights_TasksCountByDuration()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var curve = new PlannedProgressCurve(Calculate(
            [Task(a, "A", 1), Task(b, "B", 3)], [new ScheduleLinkInput(a, b, DependencyType.FinishToStart, 0)]));

        Assert.Equal(25m, curve.At(Day(0))); // A (1 of 4 days) is done
        Assert.Equal(50m, curve.At(Day(1)));
        Assert.Equal(100m, curve.At(Day(3)));
    }

    [Fact]
    public void AMilestone_CountsOnlyOnceItsDateHasPassed()
    {
        Guid a = Guid.NewGuid(), m = Guid.NewGuid();
        var activities = new[]
        {
            Task(a, "A", 2, weight: 50),
            new ScheduleActivityInput(m, null, "Gate", true, null, null, null, 50, 0, 0)
        };
        var curve = new PlannedProgressCurve(Calculate(activities, [new ScheduleLinkInput(a, m, DependencyType.FinishToStart, 0)]));

        Assert.Equal(25m, curve.At(Day(0)));
        Assert.Equal(100m, curve.At(Day(1))); // A done and the gate, which sits on A's last day, reached
    }

    [Fact]
    public void Summaries_RollTheirChildrenUp_AndNestedGroupsWork()
    {
        Guid phase = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid(), other = Guid.NewGuid();
        var curve = new PlannedProgressCurve(Calculate(
            [
                new ScheduleActivityInput(phase, null, "Phase", false, null, null, null, 50, 0, 0),
                Task(a, "A", 2, weight: 1, parent: phase), Task(b, "B", 2, weight: 1, parent: phase),
                Task(other, "Other", 4, weight: 50)
            ],
            [new ScheduleLinkInput(a, b, DependencyType.FinishToStart, 0)]));

        // After day 1: Phase = A done (50%) of itself => 50; Other = 50 => total (50*50 + 50*50)/100.
        Assert.Equal(50m, curve.At(Day(1)));
        Assert.Equal(100m, curve.At(Day(3)));
    }

    [Fact]
    public void TheCurve_NeverGoesDown_AndEndsAt100()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        var curve = new PlannedProgressCurve(Calculate(
            [Task(a, "A", 3), Task(b, "B", 2), Task(c, "C", 4), Task(d, "D", 1)],
            [
                new ScheduleLinkInput(a, b, DependencyType.FinishToStart, 0), new ScheduleLinkInput(a, c, DependencyType.StartToStart, 1),
                new ScheduleLinkInput(b, d, DependencyType.FinishToStart, 0), new ScheduleLinkInput(c, d, DependencyType.FinishToStart, 0)
            ], new Weekdays()));

        var previous = -1m;
        for (var offset = -3; offset < 40; offset++)
        {
            var value = curve.At(Day(offset));
            Assert.InRange(value, 0m, 100m);
            Assert.True(value >= previous, $"went down on day {offset}: {previous} -> {value}");
            previous = value;
        }

        Assert.Equal(100m, previous);
    }

    [Fact]
    public void AnEmptyPlan_IsAtZero()
    {
        Assert.Equal(0m, new PlannedProgressCurve(Calculate([], [])).At(Day(5)));
    }

    // ------------------------------------------------------------ the service

    private sealed record Fixture(
        ProgressCurveService Service, FakeSnapshotRepository Snapshots, FakeActivityRepository Activities,
        FakeDependencyRepository Dependencies, Project Project);

    private static Fixture Build()
    {
        var project = new Project(Guid.NewGuid(), Tenant, "Tower", "TWR", ProjectType.Waterfall);
        project.UpdateDetails("Tower", "TWR", null, null, null, null, Monday, null, null, null, null, null, null, null, null);
        var activities = new FakeActivityRepository();
        var dependencies = new FakeDependencyRepository();
        var unitOfWork = new FakeWaterfallUnitOfWork();
        var time = new FixedTimeProvider(new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var schedule = new ScheduleService(activities, dependencies, new FakeProjectRepository(project), new FakeCalendarProvider(null), unitOfWork, time);
        var snapshots = new FakeSnapshotRepository();
        return new Fixture(new ProgressCurveService(snapshots, schedule, unitOfWork, time), snapshots, activities, dependencies, project);
    }

    private static Activity Add(Fixture f, string name, int days, decimal actual = 0, decimal weight = 0)
    {
        var activity = new Activity(Guid.NewGuid(), Tenant, f.Project.Id, name);
        activity.UpdateDetails(name, null, null, null, null, null, null, null, days, null, weight);
        activity.UpdateProgress(0, actual);
        f.Activities.Items.Add(activity);
        return activity;
    }

    [Fact]
    public async Task SCurve_SamplesThePlannedLine_FromStartToFinish_IncludingTheFinish()
    {
        var f = Build();
        Add(f, "A", 10);

        var curve = (await f.Service.GetSCurveAsync(f.Project.Id, 3, default)).Value!;

        Assert.Equal((Monday, Monday.AddDays(9), 3), (curve.ProjectStart, curve.ProjectFinish, curve.StepDays));
        Assert.Equal([Day(0), Day(3), Day(6), Day(9)], curve.Planned.Select(p => p.Date));
        Assert.Equal([10m, 40m, 70m, 100m], curve.Planned.Select(p => p.PlannedProgress));
    }

    [Fact]
    public async Task SCurve_ForAOneDayProject_HasASinglePointAtItsFinish()
    {
        var f = Build();
        Add(f, "A", 1);

        var curve = (await f.Service.GetSCurveAsync(f.Project.Id, 7, default)).Value!;

        Assert.Equal([Day(0)], curve.Planned.Select(p => p.Date));
        Assert.Equal([100m], curve.Planned.Select(p => p.PlannedProgress));
    }

    [Fact]
    public async Task SCurve_ReportsWhereTheProjectStandsToday_AndItsPerformanceIndex()
    {
        var f = Build();
        Add(f, "A", 10, actual: 15); // today is day 2 => planned 30%, actual 15%

        var curve = (await f.Service.GetSCurveAsync(f.Project.Id, 7, default)).Value!;

        Assert.Equal(Today, curve.Today);
        Assert.Equal((30m, 15m, -15m, 0.5m), (curve.CurrentPlannedProgress, curve.CurrentActualProgress, curve.ProgressVariance, curve.SchedulePerformanceIndex));
    }

    [Fact]
    public async Task SCurve_HasNoPerformanceIndex_WhileNothingIsPlannedYet()
    {
        var f = Build();
        var late = Add(f, "A", 5);
        late.UpdateDetails("A", null, null, null, null, null, Monday.AddDays(30), null, 5, null, 0); // starts long after today

        var curve = (await f.Service.GetSCurveAsync(f.Project.Id, 7, default)).Value!;

        Assert.Equal(0m, curve.CurrentPlannedProgress);
        Assert.Null(curve.SchedulePerformanceIndex);
    }

    [Fact]
    public async Task SCurve_ReturnsTheSnapshotsAsTheActualLine_OldestFirst()
    {
        var f = Build();
        Add(f, "A", 10);
        await f.Service.CreateSnapshotAsync(new CreateProgressSnapshotRequest(Tenant, f.Project.Id, Day(4), null), default);
        await f.Service.CreateSnapshotAsync(new CreateProgressSnapshotRequest(Tenant, f.Project.Id, Day(1), null), default);

        var curve = (await f.Service.GetSCurveAsync(f.Project.Id, 7, default)).Value!;

        Assert.Equal([Day(1), Day(4)], curve.Actual.Select(a => a.Date));
        Assert.Equal([20m, 50m], curve.Actual.Select(a => a.PlannedProgress));
    }

    [Fact]
    public async Task SCurve_WithAVeryLongProject_IsSampledMoreCoarselyInsteadOfReturningThousands()
    {
        var f = Build();
        Add(f, "Decades", 9000);

        var curve = (await f.Service.GetSCurveAsync(f.Project.Id, 1, default)).Value!;

        Assert.InRange(curve.Planned.Count, 2, ProgressCurveService.MaxPlannedPoints + 2);
        Assert.True(curve.StepDays > 1);
        Assert.Equal(curve.ProjectFinish, curve.Planned[^1].Date);
        Assert.Equal(100m, curve.Planned[^1].PlannedProgress);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(ProgressCurveService.MaxStepDays + 1)]
    public async Task SCurve_RejectsAnOutOfRangeStep(int step)
    {
        var f = Build();
        Add(f, "A", 5);

        Assert.Equal("validation.error", (await f.Service.GetSCurveAsync(f.Project.Id, step, default)).Error.Code);
    }

    [Fact]
    public async Task SCurve_AndSnapshots_ReportAnUnknownProject()
    {
        var f = Build();

        Assert.Equal("not_found", (await f.Service.GetSCurveAsync(Guid.NewGuid(), 7, default)).Error.Code);
        Assert.Equal("not_found", (await f.Service.CreateSnapshotAsync(new CreateProgressSnapshotRequest(Tenant, Guid.NewGuid(), null, null), default)).Error.Code);
    }

    // ---------------------------------------------------------- snapshots

    [Fact]
    public async Task ASnapshot_RecordsThePlannedLineAndTheRolledUpActual_AsOfTodayByDefault()
    {
        var f = Build();
        Add(f, "A", 10, actual: 40);

        var created = await f.Service.CreateSnapshotAsync(new CreateProgressSnapshotRequest(Tenant, f.Project.Id, null, "  weekly review "), default);

        Assert.True(created.IsSuccess);
        var snapshot = created.Value!;
        Assert.Equal((Today, 30m, 40m, "weekly review"), (snapshot.SnapshotDate, snapshot.PlannedProgress, snapshot.ActualProgress, snapshot.Note));
    }

    [Fact]
    public async Task ASecondSnapshotForTheSameDate_RefreshesTheFirst()
    {
        var f = Build();
        var task = Add(f, "A", 10, actual: 10);
        var first = (await f.Service.CreateSnapshotAsync(new CreateProgressSnapshotRequest(Tenant, f.Project.Id, Day(3), "morning"), default)).Value!;

        task.UpdateProgress(0, 35);
        var second = (await f.Service.CreateSnapshotAsync(new CreateProgressSnapshotRequest(Tenant, f.Project.Id, Day(3), null), default)).Value!;

        Assert.Equal(first.Id, second.Id);
        Assert.Single(f.Snapshots.Items);
        Assert.Equal((35m, null), (second.ActualProgress, second.Note));
    }

    [Fact]
    public async Task Snapshots_AreListedOldestFirst_AndCanBeDeleted()
    {
        var f = Build();
        Add(f, "A", 10);
        var late = (await f.Service.CreateSnapshotAsync(new CreateProgressSnapshotRequest(Tenant, f.Project.Id, Day(6), null), default)).Value!;
        await f.Service.CreateSnapshotAsync(new CreateProgressSnapshotRequest(Tenant, f.Project.Id, Day(2), null), default);

        var list = (await f.Service.ListSnapshotsAsync(f.Project.Id, default)).Value!;
        Assert.Equal([Day(2), Day(6)], list.Select(s => s.SnapshotDate));

        Assert.True((await f.Service.DeleteSnapshotAsync(late.Id, default)).IsSuccess);
        Assert.Single((await f.Service.ListSnapshotsAsync(f.Project.Id, default)).Value!);
        Assert.Equal("not_found", (await f.Service.DeleteSnapshotAsync(late.Id, default)).Error.Code);
        Assert.Empty((await f.Service.ListSnapshotsAsync(Guid.NewGuid(), default)).Value!);
    }

    [Fact]
    public async Task ASnapshotBeforeTheProjectStartOrAfterItsEnd_ClampsThePlannedValue()
    {
        var f = Build();
        Add(f, "A", 4);

        var before = (await f.Service.CreateSnapshotAsync(new CreateProgressSnapshotRequest(Tenant, f.Project.Id, Day(-10), null), default)).Value!;
        var after = (await f.Service.CreateSnapshotAsync(new CreateProgressSnapshotRequest(Tenant, f.Project.Id, Day(100), null), default)).Value!;

        Assert.Equal(0m, before.PlannedProgress);
        Assert.Equal(100m, after.PlannedProgress);
    }
}
