using Nexus.Organization.Domain;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.Reporting.Analytics;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

public sealed class Phase8ReportingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 6, 1);

    private sealed class FixedTime(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    private sealed class Projects(params Project[] items) : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(items.SingleOrDefault(p => p.Id == id));
        public Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken ct) =>
            Task.FromResult(new PagedResult<Project>(items.ToList(), 1, 1000, items.Length));
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(Project project, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveAsync(Project project, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Actions : Nexus.Actions.Application.IActionItemRepository
    {
        public Task<Nexus.Actions.Domain.ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Nexus.Actions.Domain.ActionItem?>(null);
        public Task<IReadOnlyList<Nexus.Actions.Domain.ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Nexus.Actions.Domain.ActionItem>>([]);
        public Task AddAsync(Nexus.Actions.Domain.ActionItem action, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Units(params OrganizationUnit[] units) : Nexus.Organization.Application.IOrganizationUnitRepository
    {
        public Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(units.SingleOrDefault(u => u.Id == id));
        public Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrganizationUnit>>(units);
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(OrganizationUnit unit, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Plan(params (Project Project, decimal Planned, decimal Actual)[] figures) : IProjectProgressService
    {
        public Task<Result<IReadOnlyList<ProjectProgressDto>>> GetAsync(Guid tenantId, ProjectProgressRequest request, CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProjectProgressDto>>(figures
                .Where(f => request.ProjectIds.Contains(f.Project.Id))
                .Select(f => new ProjectProgressDto(f.Project.Id, f.Project.Type, f.Planned, f.Actual, f.Planned > 0 ? Math.Round(f.Actual / f.Planned * 100m, 2) : 100m))
                .ToList()));
    }

    private static Project NewProject(string code, Guid unit, Guid manager, ProjectStatus status = ProjectStatus.Active, bool approved = true)
    {
        var project = new Project(Guid.NewGuid(), Tenant, code, code, ProjectType.Waterfall, manager, null);
        project.UpdateDetails(code, code, manager, null, unit, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 100m, null, null, null, null, null, null);
        project.ChangeStatus(status);
        if (approved)
        {
            project.MarkPendingApproval();
            project.Approve();
        }

        return project;
    }

    private static AnalyticsService Service(Project[] projects, OrganizationUnit[] units, Plan? plan, AnalyticsOptions options) =>
        new(new Projects(projects), new Actions(), null, null, new Units(units), null, null, new FixedTime(Today), plan, options);

    [Fact]
    public async Task WithTheOptionsOn_UnitsAreRatedFromPlannedVersusDoneFigures_OfApprovedProjectsOnly()
    {
        var head = new OrganizationUnit(Guid.NewGuid(), Tenant, "Head", "H", null);
        var eng = new OrganizationUnit(Guid.NewGuid(), Tenant, "Eng", "E", head.Id);
        var good = NewProject("GOOD", eng.Id, Alice);
        var late = NewProject("LATE", eng.Id, Alice);
        var draft = NewProject("DRAFT", eng.Id, Bob, approved: false);
        var plan = new Plan((good, 50, 50), (late, 50, 20), (draft, 50, 0));

        var report = (await Service([good, late, draft], [head, eng], plan, new AnalyticsOptions { ProgressFromProjectPlan = true, OnlyApprovedProjects = true })
            .GetUnitPerformanceAsync(Tenant, 2, null, default)).Value!;

        var unit = Assert.Single(report.Units, u => u.UnitName == "Eng");
        Assert.Equal(2, unit.ProjectCount); // the draft is left out
        Assert.Equal(1, unit.DelayedProjects); // LATE: 20 done against 50 planned
        Assert.Equal(70m, unit.AveragePerformance); // (100 + 40) / 2
        Assert.Equal("Good", unit.Rating);
        Assert.Equal(new[] { good.Id, late.Id }.Order(), unit.ProjectIds!.Order());
        Assert.Equal(1, unit.OnTrack);
        Assert.Equal(1, unit.Behind);
        Assert.Equal((2, 2, 1, 70m, "Good"), (report.Summary!.ProjectCount, report.Summary.RunningProjects, report.Summary.DelayedProjects, report.Summary.AveragePerformance, report.Summary.Rating));
    }

    [Fact]
    public async Task ManagersGetTheSameFiguresAndRating_AndTheMatrixListsWhichProjectsAreInEachCell()
    {
        var head = new OrganizationUnit(Guid.NewGuid(), Tenant, "Head", "H", null);
        var eng = new OrganizationUnit(Guid.NewGuid(), Tenant, "Eng", "E", head.Id);
        var a = NewProject("A", eng.Id, Alice);
        var b = NewProject("B", eng.Id, Alice, ProjectStatus.Completed);
        var c = NewProject("C", eng.Id, Bob);
        var plan = new Plan((a, 50, 50), (b, 100, 100), (c, 50, 10));
        var service = Service([a, b, c], [head, eng], plan, new AnalyticsOptions { ProgressFromProjectPlan = true });

        var managers = (await service.GetProjectManagerEvaluationAsync(Tenant, null, default)).Value!.Managers;
        var alice = managers.Single(m => m.ManagerUserId == Alice);
        Assert.Equal("Excellent", alice.Rating);
        Assert.Equal(0, alice.DelayedProjects);
        var bob = managers.Single(m => m.ManagerUserId == Bob);
        Assert.Equal("NeedsImprovement", bob.Rating);
        Assert.Equal(1, bob.DelayedProjects);

        var matrix = (await service.GetUnitStatusMatrixAsync(Tenant, 2, default)).Value!;
        var row = matrix.Rows.Single(r => r.UnitName == "Eng");
        var completed = matrix.Statuses.ToList().IndexOf(ProjectStatus.Completed.ToString());
        Assert.Equal([b.Id], row.ProjectIdsByStatus![completed]);
        Assert.Equal(row.Counts.Select((count, i) => (count, ids: row.ProjectIdsByStatus![i].Count)).Select(x => x.count), row.ProjectIdsByStatus!.Select(ids => ids.Count));
    }

    [Fact]
    public async Task WithTheOptionsOff_NothingChanges_ProgressStillComesFromRegisteredUpdates_AndDraftsAreIncluded()
    {
        var head = new OrganizationUnit(Guid.NewGuid(), Tenant, "Head", "H", null);
        var eng = new OrganizationUnit(Guid.NewGuid(), Tenant, "Eng", "E", head.Id);
        var draft = NewProject("DRAFT", eng.Id, Alice, approved: false);
        var plan = new Plan((draft, 50, 0));

        var report = (await Service([draft], [head, eng], plan, new AnalyticsOptions()).GetUnitPerformanceAsync(Tenant, 2, null, default)).Value!;

        var unit = Assert.Single(report.Units, u => u.UnitName == "Eng");
        Assert.Equal(1, unit.ProjectCount);
        Assert.Equal(0, unit.ProjectsWithProgress); // the plan is ignored: there is no Progress module here, so nothing is known
        Assert.Null(unit.AveragePerformance);
        Assert.Null(unit.Rating);
    }

    [Theory]
    [InlineData(95, "Excellent")]
    [InlineData(90, "Excellent")]
    [InlineData(89.99, "Good")]
    [InlineData(70, "Good")]
    [InlineData(69.9, "NeedsImprovement")]
    public void TheRating_FollowsTheNinetyAndSeventyBands(double average, string expected) =>
        Assert.Equal(expected, PerformanceRating.Of((decimal)average));

    // ----------------------------------------------------- sprint progress

    [Fact]
    public async Task ProjectSprintProgress_CountsPerSprint_WithRunningTotals_AndTasksWithoutASprintInTheFirst()
    {
        var f = new AgileFixture();
        await f.AddSprintAsync(name: "One");
        await f.AddSprintAsync();
        await f.AddSprintAsync();
        await f.AddTaskAsync("a", points: 3, sprint: 1, status: AgileTaskStatus.Done);
        await f.AddTaskAsync("b", points: 2, sprint: 1);
        await f.AddTaskAsync("c", points: 5, sprint: 2, status: AgileTaskStatus.Done);
        await f.AddTaskAsync("d", points: 1, sprint: null);
        var service = new SprintChartService(f.Sprints, f.Events, f.Clock, f.Tasks);

        var byCount = (await service.GetProjectProgressAsync(f.ProjectId, ChartMetric.Count, default)).Value!.Sprints;
        var byPoints = (await service.GetProjectProgressAsync(f.ProjectId, ChartMetric.Points, default)).Value!.Sprints;

        Assert.Equal(3, byCount.Count);
        Assert.Equal("One", byCount[0].Name);
        Assert.Equal((3, 1, 3, 1, 2), (byCount[0].SprintTotal, byCount[0].SprintCompleted, byCount[0].CumulativeTotal, byCount[0].CumulativeCompleted, byCount[0].CumulativeRemaining));
        Assert.Equal((4, 2, 1), (byCount[1].CumulativeTotal, byCount[1].CumulativeCompleted, byCount[1].SprintTotal - 0));
        Assert.Equal((4, 2, 2), (byCount[2].CumulativeTotal, byCount[2].CumulativeCompleted, byCount[2].CumulativeRemaining));
        Assert.Equal((11, 8, 3), (byPoints[2].CumulativeTotal, byPoints[2].CumulativeCompleted, byPoints[2].CumulativeRemaining));
    }

    [Fact]
    public async Task ProjectSprintProgress_WithNoTasksOrSprints_IsOneEmptySprint_AndNeedsTheTaskRepository()
    {
        var f = new AgileFixture();
        var service = new SprintChartService(f.Sprints, f.Events, f.Clock, f.Tasks);

        var empty = (await service.GetProjectProgressAsync(f.ProjectId, ChartMetric.Count, default)).Value!.Sprints;

        Assert.Equal((1, 0, 0), (empty.Single().SprintNumber, empty.Single().CumulativeTotal, empty.Single().CumulativeCompleted));
        Assert.Equal("conflict", (await new SprintChartService(f.Sprints, f.Events, f.Clock).GetProjectProgressAsync(f.ProjectId, ChartMetric.Count, default)).Error.Code);
    }
}
