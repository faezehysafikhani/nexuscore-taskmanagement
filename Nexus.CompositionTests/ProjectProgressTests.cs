using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Domain;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.CompositionTests;

public sealed class ProjectProgressTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    private static Project NewProject(ProjectType type, DateOnly? start = null, DateOnly? end = null, Guid? tenant = null)
    {
        var project = new Project(Guid.NewGuid(), tenant ?? Tenant, "P" + Guid.NewGuid().ToString("N")[..6], Guid.NewGuid().ToString("N")[..6], type);
        project.UpdateDetails(project.Name, project.Code, null, null, null, null, start, end, null, null, null, null, null, null, null);
        return project;
    }

    private sealed class FixedSource(params ProjectProgressFigures[] figures) : IProjectProgressSource
    {
        public Task<IReadOnlyList<ProjectProgressFigures>> GetProgressAsync(IReadOnlyCollection<Project> projects, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProjectProgressFigures>>(figures.Where(f => projects.Any(p => p.Id == f.ProjectId)).ToList());
    }

    private static ProjectProgressService Service(Project[] projects, params IProjectProgressSource[] sources) =>
        new(new FakeProjectRepository(projects), sources, new FixedTimeProvider(Now));

    [Fact]
    public async Task WithoutAnySource_AProjectIsReportedFromItsDatesAlone()
    {
        // 1 Mar -> 31 Mar, "today" 15 Mar: 14 of 30 days have gone by.
        var project = NewProject(ProjectType.Waterfall, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var result = (await Service([project]).GetAsync(Tenant, new ProjectProgressRequest([project.Id]), default)).Value!.Single();

        Assert.Equal(46.67m, result.PlannedProgress);
        Assert.Equal((0m, 0m), (result.ActualProgress, result.Performance));
        Assert.Null(result.TotalTasks);
    }

    [Fact]
    public async Task ASourcesFigures_ReplaceTheDatesFallback_AndPerformanceIsActualOverPlanned()
    {
        var project = NewProject(ProjectType.Waterfall, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var result = (await Service([project], new FixedSource(new ProjectProgressFigures(project.Id, 50m, 25m)))
            .GetAsync(Tenant, new ProjectProgressRequest([project.Id]), default)).Value!.Single();

        Assert.Equal((50m, 25m, 50m), (result.PlannedProgress, result.ActualProgress, result.Performance));
    }

    [Fact]
    public async Task Performance_IsAHundredWhenNothingWasDueYetButSomethingIsDone_AndZeroWhenNeither()
    {
        var future = NewProject(ProjectType.Agile, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1));
        var quiet = NewProject(ProjectType.Agile, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1));
        var service = Service([future, quiet], new FixedSource(new ProjectProgressFigures(future.Id, null, 20m)));

        var results = (await service.GetAsync(Tenant, new ProjectProgressRequest([future.Id, quiet.Id]), default)).Value!;

        Assert.Equal(100m, results.Single(r => r.ProjectId == future.Id).Performance);
        Assert.Equal(0m, results.Single(r => r.ProjectId == quiet.Id).Performance);
    }

    [Fact]
    public async Task UnknownProjectsAndOtherTenantsProjects_AreLeftOut()
    {
        var mine = NewProject(ProjectType.Waterfall);
        var theirs = NewProject(ProjectType.Waterfall, tenant: Guid.NewGuid());

        var results = (await Service([mine, theirs]).GetAsync(Tenant, new ProjectProgressRequest([mine.Id, theirs.Id, Guid.NewGuid(), mine.Id]), default)).Value!;

        Assert.Equal([mine.Id], results.Select(r => r.ProjectId));
    }

    [Fact]
    public async Task TheWaterfallSource_ReportsTheScheduleRollUp_AndSkipsProjectsWithoutActivities()
    {
        var withPlan = NewProject(ProjectType.Waterfall, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 31));
        var empty = NewProject(ProjectType.Waterfall, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 31));
        var agile = NewProject(ProjectType.Agile);
        var activities = new FakeActivityRepository();
        var activity = new Activity(Guid.NewGuid(), Tenant, withPlan.Id, "A");
        activity.UpdateDetails("A", null, null, null, null, null, null, null, 4, null, 0);
        activity.UpdateProgress(0, 30);
        activities.Items.Add(activity);
        var schedule = new ScheduleService(
            activities, new FakeDependencyRepository(), new FakeProjectRepository(withPlan, empty), new FakeCalendarProvider(null),
            new FakeWaterfallUnitOfWork(), new FixedTimeProvider(Now));

        var figures = await new WaterfallProjectProgressSource(schedule).GetProgressAsync([withPlan, empty, agile], default);

        var only = Assert.Single(figures);
        Assert.Equal((withPlan.Id, 30m), (only.ProjectId, only.Actual));
    }

    [Fact]
    public async Task TheAgileSource_CountsTasksPerColumn_AndLeavesPlannedToTheProjectDates()
    {
        var project = NewProject(ProjectType.Agile);
        var other = NewProject(ProjectType.Waterfall);
        var tasks = new FakeAgileTaskRepository();
        foreach (var status in new[] { AgileTaskStatus.ToDo, AgileTaskStatus.InProgress, AgileTaskStatus.UnderReview, AgileTaskStatus.Done })
        {
            var task = new AgileTask(Guid.NewGuid(), Tenant, project.Id, status.ToString());
            task.ChangeStatus(status);
            tasks.Items.Add(task);
        }

        var figures = await new AgileProjectProgressSource(tasks).GetProgressAsync([project, other], default);

        var row = Assert.Single(figures);
        Assert.Equal((null, 25m), (row.Planned, row.Actual));
        Assert.Equal((4, 1, 2, 1), (row.TotalTasks, row.TodoTasks, row.DoingTasks, row.DoneTasks));
    }
}
