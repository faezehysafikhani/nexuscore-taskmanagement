using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.CompositionTests;

public sealed class ScheduleBaselineTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private sealed record Fixture(
        ScheduleBaselineService Baselines, ScheduleService Schedule, FakeActivityRepository Activities,
        FakeDependencyRepository Dependencies, FakeBaselineRepository Repository, Project Project);

    private static Fixture Build()
    {
        var project = new Project(Guid.NewGuid(), Tenant, "Tower", "TWR", ProjectType.Waterfall);
        project.UpdateDetails("Tower", "TWR", null, null, null, null, Monday, null, null, null, null, null, null, null, null);
        var activities = new FakeActivityRepository();
        var dependencies = new FakeDependencyRepository();
        var unitOfWork = new FakeWaterfallUnitOfWork();
        var schedule = new ScheduleService(activities, dependencies, new FakeProjectRepository(project),
            new FakeCalendarProvider(null), unitOfWork, new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var repository = new FakeBaselineRepository();
        return new Fixture(new ScheduleBaselineService(repository, schedule, unitOfWork), schedule, activities, dependencies, repository, project);
    }

    private static Activity Add(Fixture f, string name, int days, Guid? parent = null)
    {
        var activity = new Activity(Guid.NewGuid(), Tenant, f.Project.Id, name, parent);
        activity.UpdateDetails(name, null, parent, null, null, null, null, null, days, null, 0);
        f.Activities.Items.Add(activity);
        return activity;
    }

    private static void Link(Fixture f, Activity a, Activity b) =>
        f.Dependencies.Items.Add(new ActivityDependency(Guid.NewGuid(), Tenant, f.Project.Id, a.Id, b.Id, DependencyType.FinishToStart, 0));

    private static CreateScheduleBaselineRequest Request(Fixture f, string name = "Approved plan", string? note = null) =>
        new(Tenant, f.Project.Id, name, note);

    [Fact]
    public async Task CreatingABaseline_FreezesTheCalculatedSchedule_NotJustTheStoredDates()
    {
        var f = Build();
        var a = Add(f, "A", 3);
        var b = Add(f, "B", 2);
        Link(f, a, b); // B has no stored dates; the baseline must still know it starts on day 3

        var created = await f.Baselines.CreateAsync(Request(f, note: "  signed off  "), default);

        Assert.True(created.IsSuccess);
        var detail = created.Value!;
        Assert.Equal((1, "Approved plan", "signed off"), (detail.Baseline.Number, detail.Baseline.Name, detail.Baseline.Note));
        Assert.Equal((Monday, Monday.AddDays(4)), (detail.Baseline.ProjectStart, detail.Baseline.ProjectFinish));
        Assert.Equal(2, detail.Baseline.ActivityCount);
        var rowB = detail.Activities.Single(x => x.ActivityId == b.Id);
        Assert.Equal((Monday.AddDays(3), Monday.AddDays(4), 2), (rowB.StartDate, rowB.EndDate, rowB.DurationDays));
    }

    [Fact]
    public async Task ABaseline_IsNotAffectedByLaterChangesToThePlan()
    {
        var f = Build();
        var a = Add(f, "A", 3);
        var created = (await f.Baselines.CreateAsync(Request(f), default)).Value!;

        a.UpdateDetails("A renamed", null, null, null, null, null, null, null, 10, null, 0);
        f.Activities.Items.Remove(a);

        var reread = (await f.Baselines.GetAsync(created.Baseline.Id, default)).Value!;
        var row = Assert.Single(reread.Activities);
        Assert.Equal(("A", 3), (row.Name, row.DurationDays));
    }

    [Fact]
    public async Task Numbers_AreHighestPlusOne_SoAGapInTheMiddleIsNeverRefilled()
    {
        var f = Build();
        Add(f, "A", 1);

        var first = (await f.Baselines.CreateAsync(Request(f, "one"), default)).Value!;
        var second = (await f.Baselines.CreateAsync(Request(f, "two"), default)).Value!;
        Assert.Equal((1, 2), (first.Baseline.Number, second.Baseline.Number));

        Assert.True((await f.Baselines.DeleteAsync(second.Baseline.Id, default)).IsSuccess);
        var third = (await f.Baselines.CreateAsync(Request(f, "three"), default)).Value!;
        Assert.Equal(2, third.Baseline.Number); // the latest one was deleted, so its number comes back

        Assert.True((await f.Baselines.DeleteAsync(first.Baseline.Id, default)).IsSuccess);
        var fourth = (await f.Baselines.CreateAsync(Request(f, "four"), default)).Value!;
        Assert.Equal(3, fourth.Baseline.Number); // 1 is gone from the middle of the sequence and is not refilled
    }

    [Fact]
    public async Task Delete_RemovesTheBaselineAndItsRows()
    {
        var f = Build();
        Add(f, "A", 1);
        Add(f, "B", 1);
        var created = (await f.Baselines.CreateAsync(Request(f), default)).Value!;
        Assert.Equal(2, f.Repository.Rows.Count);

        Assert.True((await f.Baselines.DeleteAsync(created.Baseline.Id, default)).IsSuccess);

        Assert.Empty(f.Repository.Baselines);
        Assert.Empty(f.Repository.Rows);
        Assert.Equal("not_found", (await f.Baselines.DeleteAsync(created.Baseline.Id, default)).Error.Code);
        Assert.Equal("not_found", (await f.Baselines.GetAsync(created.Baseline.Id, default)).Error.Code);
    }

    [Fact]
    public async Task List_ReturnsBaselinesInNumberOrder_WithTheirActivityCounts()
    {
        var f = Build();
        Add(f, "A", 1);
        await f.Baselines.CreateAsync(Request(f, "one"), default);
        Add(f, "B", 1);
        await f.Baselines.CreateAsync(Request(f, "two"), default);

        var list = (await f.Baselines.ListByProjectAsync(f.Project.Id, default)).Value!;

        Assert.Equal([("one", 1), ("two", 2)], list.Select(b => (b.Name, b.ActivityCount)));
        Assert.Empty((await f.Baselines.ListByProjectAsync(Guid.NewGuid(), default)).Value!);
    }

    [Fact]
    public async Task Create_RefusesBlankNames_EmptyPlans_UnknownProjects_AndTooManyBaselines()
    {
        var f = Build();
        Assert.Equal("validation.error", (await f.Baselines.CreateAsync(Request(f, "  "), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Baselines.CreateAsync(Request(f), default)).Error.Code); // no activities yet

        Add(f, "A", 1);
        Assert.Equal("not_found", (await f.Baselines.CreateAsync(new CreateScheduleBaselineRequest(Tenant, Guid.NewGuid(), "x", null), default)).Error.Code);

        for (var number = 1; number <= ScheduleBaselineService.MaxBaselinesPerProject; number++)
        {
            f.Repository.Baselines.Add(new ScheduleBaseline(Guid.NewGuid(), Tenant, f.Project.Id, number, $"b{number}", null, Monday, Monday));
        }

        Assert.Equal("conflict", (await f.Baselines.CreateAsync(Request(f), default)).Error.Code);
    }

    // ------------------------------------------------------------ variance

    [Fact]
    public async Task Variance_ShowsLateEarlyAndOnTrackActivities_AndTheProjectSlip()
    {
        var f = Build();
        var a = Add(f, "A", 3);
        var b = Add(f, "B", 2);
        var c = Add(f, "C", 4);
        Link(f, a, b);
        var baseline = (await f.Baselines.CreateAsync(Request(f), default)).Value!.Baseline;

        // A grows by 2 days (so B slips 2 days), C shrinks by 1, nothing else moves.
        a.UpdateDetails("A", null, null, null, null, null, null, null, 5, null, 0);
        c.UpdateDetails("C", null, null, null, null, null, null, null, 3, null, 0);

        var variance = (await f.Baselines.GetVarianceAsync(baseline.Id, default)).Value!;

        var rowA = variance.Activities.Single(x => x.ActivityId == a.Id);
        Assert.Equal((VarianceStatus.Late, 0, 2, 2), (rowA.Status, rowA.StartVarianceDays, rowA.FinishVarianceDays, rowA.DurationVarianceDays));
        var rowB = variance.Activities.Single(x => x.ActivityId == b.Id);
        Assert.Equal((VarianceStatus.Late, 2, 2, 0), (rowB.Status, rowB.StartVarianceDays, rowB.FinishVarianceDays, rowB.DurationVarianceDays));
        var rowC = variance.Activities.Single(x => x.ActivityId == c.Id);
        Assert.Equal((VarianceStatus.Early, -1, -1), (rowC.Status, rowC.FinishVarianceDays, rowC.DurationVarianceDays));

        // Project: baseline finished day 4 (B, A->B = 5 days; C = 4 days); now B ends day 6.
        Assert.Equal((0, 2), (variance.ProjectStartVarianceDays, variance.ProjectFinishVarianceDays));
        Assert.Equal((0, 2, 1, 0, 0), (variance.OnTrackCount, variance.LateCount, variance.EarlyCount, variance.AddedCount, variance.RemovedCount));
    }

    [Fact]
    public async Task Variance_OfAnUnchangedPlan_IsZeroEverywhere()
    {
        var f = Build();
        var a = Add(f, "A", 3);
        Link(f, a, Add(f, "B", 2));
        var baseline = (await f.Baselines.CreateAsync(Request(f), default)).Value!.Baseline;

        var variance = (await f.Baselines.GetVarianceAsync(baseline.Id, default)).Value!;

        Assert.All(variance.Activities, row =>
        {
            Assert.Equal(VarianceStatus.OnTrack, row.Status);
            Assert.Equal((0, 0, 0), (row.StartVarianceDays, row.FinishVarianceDays, row.DurationVarianceDays));
        });
        Assert.Equal((0, 0), (variance.ProjectStartVarianceDays, variance.ProjectFinishVarianceDays));
        Assert.Equal(2, variance.OnTrackCount);
    }

    [Fact]
    public async Task Variance_ReportsAddedAndRemovedActivities()
    {
        var f = Build();
        var kept = Add(f, "Kept", 2);
        var dropped = Add(f, "Dropped", 2);
        var baseline = (await f.Baselines.CreateAsync(Request(f), default)).Value!.Baseline;

        f.Activities.Items.Remove(dropped);
        var added = Add(f, "Added", 5);

        var variance = (await f.Baselines.GetVarianceAsync(baseline.Id, default)).Value!;

        var rowAdded = variance.Activities.Single(x => x.ActivityId == added.Id);
        Assert.Equal(VarianceStatus.Added, rowAdded.Status);
        Assert.Null(rowAdded.BaselineStart);
        Assert.Null(rowAdded.FinishVarianceDays);
        Assert.NotNull(rowAdded.CurrentStart);

        var rowRemoved = variance.Activities.Single(x => x.ActivityId == dropped.Id);
        Assert.Equal(VarianceStatus.Removed, rowRemoved.Status);
        Assert.Null(rowRemoved.CurrentStart);
        Assert.NotNull(rowRemoved.BaselineStart);

        Assert.Equal(VarianceStatus.OnTrack, variance.Activities.Single(x => x.ActivityId == kept.Id).Status);
        Assert.Equal((1, 1), (variance.AddedCount, variance.RemovedCount));
    }

    [Fact]
    public async Task Variance_IncludesSummaryActivities()
    {
        var f = Build();
        var phase = Add(f, "Phase", 1);
        phase.UpdateDetails("Phase", null, null, null, null, null, null, null, null, null, 0);
        var child = Add(f, "Task", 3, parent: phase.Id);
        var baseline = (await f.Baselines.CreateAsync(Request(f), default)).Value!.Baseline;

        child.UpdateDetails("Task", null, phase.Id, null, null, null, null, null, 6, null, 0);
        var variance = (await f.Baselines.GetVarianceAsync(baseline.Id, default)).Value!;

        var summary = variance.Activities.Single(x => x.ActivityId == phase.Id);
        Assert.True(summary.IsSummary);
        Assert.Equal((VarianceStatus.Late, 3), (summary.Status, summary.FinishVarianceDays));
    }

    [Fact]
    public async Task Variance_OfAnUnknownBaseline_IsNotFound()
    {
        Assert.Equal("not_found", (await Build().Baselines.GetVarianceAsync(Guid.NewGuid(), default)).Error.Code);
    }
}
