using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Kpi.Application;
using Nexus.ProjectManagement.Kpi.Domain;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Domain;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using Nexus.ProjectManagement.RiskManagement.Domain;
using Nexus.Reporting.Analytics;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

public sealed class ManagementBoardTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 6, 1);

    // ----------------------------------------------------------------- fakes

    private sealed class FakeAnalytics(params ProjectPerformanceDto[] rows) : IAnalyticsService
    {
        public Task<Result<IReadOnlyList<ProjectPerformanceDto>>> GetProjectsPerformanceAsync(Guid tenantId, DateOnly? asOf, CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProjectPerformanceDto>>(rows));
        public Task<Result<ProjectPerformanceDto>> GetProjectPerformanceAsync(Guid tenantId, Guid projectId, DateOnly? asOf, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<UnitPerformanceReportDto>> GetUnitPerformanceAsync(Guid tenantId, int level, DateOnly? asOf, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<UnitStatusMatrixDto>> GetUnitStatusMatrixAsync(Guid tenantId, int level, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ProjectManagerEvaluationReportDto>> GetProjectManagerEvaluationAsync(Guid tenantId, DateOnly? asOf, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<StrategyAlignmentMatrixDto>> GetStrategyAlignmentMatrixAsync(Guid tenantId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeActions(params ActionItem[] actions) : IActionItemRepository
    {
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(actions.SingleOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ActionItem>>(actions);
        public Task AddAsync(ActionItem action, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeRisks(params RiskDto[] risks) : IRiskService
    {
        public Task<Result<IReadOnlyList<RiskDto>>> ListForProjectsAsync(ListRisksForProjectsRequest request, CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<RiskDto>>(risks.Where(r => request.ProjectIds.Contains(r.ProjectId)).ToList()));
        public Task<Result<IReadOnlyList<RiskDto>>> ListByProjectAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<RiskMatrixDto>> GetMatrixAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<RiskDto>> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<RiskDto>> CreateAsync(CreateRiskRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<RiskDto>> UpdateAsync(Guid id, UpdateRiskRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<RiskDto>> SubmitForApprovalAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeKpis(params KpiDefinition[] kpis) : IKpiRepository
    {
        public Task<KpiDefinition?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(kpis.SingleOrDefault(k => k.Id == id));
        public Task<IReadOnlyList<KpiDefinition>> ListByProjectAsync(Guid projectId, Guid? deliverableId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KpiDefinition>>(kpis.Where(k => k.ProjectId == projectId).ToList());
        public Task<IReadOnlyList<KpiDefinition>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KpiDefinition>>(kpis.Where(k => projectIds.Contains(k.ProjectId)).ToList());
        public Task AddAsync(KpiDefinition kpi, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveAsync(KpiDefinition kpi, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeMeasurements(params KpiMeasurement[] measurements) : IKpiMeasurementRepository
    {
        public Task<KpiMeasurement?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(measurements.SingleOrDefault(m => m.Id == id));
        public Task<IReadOnlyList<KpiMeasurement>> ListByKpiAsync(Guid kpiDefinitionId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KpiMeasurement>>(measurements.Where(m => m.KpiDefinitionId == kpiDefinitionId).ToList());
        public Task<IReadOnlyList<KpiMeasurement>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KpiMeasurement>>(measurements.Where(m => projectIds.Contains(m.ProjectId)).ToList());
        public Task AddAsync(KpiMeasurement measurement, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveAsync(KpiMeasurement measurement, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveByKpiAsync(Guid kpiDefinitionId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedTime(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    // --------------------------------------------------------------- builders

    private static ProjectPerformanceDto Perf(
        string code, string status = "Active", HealthStatus health = HealthStatus.Green, bool overdue = false, DateOnly? end = null) =>
        new(ProjectId: Guid.NewGuid(), Name: "Project " + code, Code: code, Status: status, OrganizationUnitId: null, ManagerUserId: Manager,
            StartDate: null, EndDate: end, AsOf: Today,
            PlannedProgress: null, ActualProgress: null, ProgressDate: null,
            BudgetAtCompletion: null, PlannedValue: null, EarnedValue: null, ScheduleVariance: null, Spi: null,
            ActualCost: null, CostVariance: null, Cpi: null,
            EstimateAtCompletion: null, EstimateToComplete: null, VarianceAtCompletion: null,
            ForecastEnd: null, ForecastDelayDays: null, IsOverdue: overdue, Health: health,
            ContractedAmount: null, InvoicedAmount: null, PaidAmount: null, ContractsAvailable: false, ProgressAvailable: false);

    private static RiskDto Risk(
        Guid projectId, string level = "red", string? plan = null, RiskResponseStatus response = RiskResponseStatus.Planning,
        ApprovalStatus approval = ApprovalStatus.Approved, int rpn = 25, string description = "risk") =>
        new(Guid.NewGuid(), Tenant, projectId, description, 5, 5, 5, rpn, plan, null, approval, null, DateTimeOffset.UtcNow,
            Level: level, ResponseStatus: response);

    private static ActionItem Action(string title, DateOnly? end, ActionStatus status = ActionStatus.Open, Guid? projectId = null)
    {
        var action = new ActionItem(Guid.NewGuid(), Tenant, title, Guid.NewGuid(), Guid.NewGuid(), projectId);
        action.UpdateDetails(title, null, null, Manager, Guid.NewGuid(), Guid.NewGuid(), projectId, null, end);
        action.ChangeStatus(status);
        return action;
    }

    private static KpiDefinition KpiWithTarget(Guid projectId, decimal? target, string description = "kpi")
    {
        var kpi = new KpiDefinition(Guid.NewGuid(), Tenant, projectId, Guid.NewGuid(), KpiType.Lag, description);
        kpi.UpdateDetails(description, null, target);
        return kpi;
    }

    private static KpiMeasurement Measured(KpiDefinition kpi, DateOnly on, decimal value) =>
        new(Guid.NewGuid(), Tenant, kpi.ProjectId, kpi.Id, on, value, null);

    private static ManagementBoardService Service(
        ProjectPerformanceDto[] projects, ActionItem[]? actions = null, IRiskService? risks = null,
        IKpiRepository? kpis = null, IKpiMeasurementRepository? measurements = null) =>
        new(new FakeAnalytics(projects), new FakeActions(actions ?? []), risks, kpis, measurements, new FixedTime(Today));

    // ------------------------------------------------------------------ health

    [Fact]
    public async Task Health_CoversRunningProjectsOnly_WorstFirst_AndCountsEveryStatus()
    {
        var green = Perf("A", health: HealthStatus.Green);
        var red = Perf("B", health: HealthStatus.Red);
        var amber = Perf("C", status: "OnHold", health: HealthStatus.Amber);
        var done = Perf("D", status: "Completed", health: HealthStatus.Red);
        var draft = Perf("E", status: "Draft", health: HealthStatus.Red);
        var unknown = Perf("F", health: HealthStatus.Unknown);

        var board = (await Service([green, red, amber, done, draft, unknown]).GetHealthBoardAsync(Tenant, null, default)).Value!;

        Assert.Equal(4, board.ProjectCount);
        Assert.Equal(["B", "C", "F", "A"], board.Projects.Select(p => p.Code).ToArray()); // red, amber, unknown, green
        Assert.Equal(["Green", "Amber", "Red", "Unknown"], board.Counts.Select(c => c.Name).ToArray());
        Assert.Equal([1, 1, 1, 1], board.Counts.Select(c => c.Count).ToArray());
        Assert.Equal(Today, board.AsOf);
    }

    [Fact]
    public async Task Health_WithNoRunningProjects_StillListsAllFourStatusesAtZero()
    {
        var board = (await Service([Perf("A", status: "Completed")]).GetHealthBoardAsync(Tenant, null, default)).Value!;

        Assert.Empty(board.Projects);
        Assert.Equal([0, 0, 0, 0], board.Counts.Select(c => c.Count).ToArray());
    }

    // --------------------------------------------------------------- attention

    [Fact]
    public async Task Attention_FlagsOverdueRedAndAmberProjects_WithTheDaysLate()
    {
        var overdue = Perf("A", overdue: true, end: new DateOnly(2026, 5, 22), health: HealthStatus.Red);
        var amber = Perf("B", health: HealthStatus.Amber);
        var fine = Perf("C");

        var board = (await Service([overdue, amber, fine]).GetAttentionBoardAsync(Tenant, null, default)).Value!;

        Assert.Equal(3, board.Total);
        var late = Assert.Single(board.Items, i => i.Kind == AttentionKinds.ProjectOverdue);
        Assert.Equal(10, late.DaysLate);
        Assert.Equal(AttentionSeverity.High, late.Severity);
        Assert.Equal("Project", late.SubjectType);
        Assert.Equal(overdue.ProjectId, late.SubjectId);
        Assert.Contains(board.Items, i => i.Kind == AttentionKinds.ProjectRed && i.SubjectId == overdue.ProjectId);
        Assert.Contains(board.Items, i => i.Kind == AttentionKinds.ProjectAtRisk && i.Severity == AttentionSeverity.Medium);
        Assert.DoesNotContain(board.Items, i => i.SubjectId == fine.ProjectId);
        Assert.False(board.RisksIncluded);
    }

    [Fact]
    public async Task Attention_FlagsOnlyApprovedOpenHighRisksWithoutAPlan()
    {
        var project = Perf("A");
        var risks = new FakeRisks(
            Risk(project.ProjectId, description: "no plan"),
            Risk(project.ProjectId, plan: "mitigate", description: "has plan"),
            Risk(project.ProjectId, response: RiskResponseStatus.Completed, description: "already handled"),
            Risk(project.ProjectId, level: "green", description: "low"),
            Risk(project.ProjectId, approval: ApprovalStatus.NotSubmitted, description: "draft"));

        var board = (await Service([project], risks: risks).GetAttentionBoardAsync(Tenant, null, default)).Value!;

        var item = Assert.Single(board.Items);
        Assert.Equal(AttentionKinds.RiskWithoutResponsePlan, item.Kind);
        Assert.Equal("Risk", item.SubjectType);
        Assert.Equal("no plan", item.Title);
        Assert.True(board.RisksIncluded);
    }

    [Fact]
    public async Task Attention_ListsOverdueOpenActions_OfRunningProjectsAndOrganisationWideOnes()
    {
        var running = Perf("A");
        var done = Perf("B", status: "Completed");
        var actions = new[]
        {
            Action("late in project", new DateOnly(2026, 5, 20), projectId: running.ProjectId),
            Action("late org-wide", new DateOnly(2026, 5, 31)),
            Action("not late yet", new DateOnly(2026, 6, 1), projectId: running.ProjectId),
            Action("late but done", new DateOnly(2026, 5, 1), ActionStatus.Completed, running.ProjectId),
            Action("late in finished project", new DateOnly(2026, 5, 1), projectId: done.ProjectId),
            Action("no end date", null)
        };

        var board = (await Service([running, done], actions).GetAttentionBoardAsync(Tenant, null, default)).Value!;

        Assert.Equal(["late in project", "late org-wide"], board.Items.Where(i => i.Kind == AttentionKinds.ActionOverdue).Select(i => i.Title).ToArray()); // most late first
        Assert.Equal(12, board.Items.First(i => i.Title == "late in project").DaysLate);
        Assert.Equal("Project A", board.Items.First(i => i.Title == "late in project").ProjectName);
        Assert.Null(board.Items.First(i => i.Title == "late org-wide").ProjectName);
    }

    [Fact]
    public async Task Attention_PutsHighBeforeMedium_AndCountsEveryKind()
    {
        var red = Perf("A", health: HealthStatus.Red);
        var board = (await Service([red], [Action("late", new DateOnly(2026, 5, 1))]).GetAttentionBoardAsync(Tenant, null, default)).Value!;

        Assert.Equal([AttentionSeverity.High, AttentionSeverity.Medium], board.Items.Select(i => i.Severity).ToArray());
        Assert.Equal(5, board.CountsByKind.Count);
        Assert.Equal(1, board.CountsByKind.Single(c => c.Name == AttentionKinds.ProjectRed).Count);
        Assert.Equal(0, board.CountsByKind.Single(c => c.Name == AttentionKinds.ProjectOverdue).Count);
    }

    [Fact]
    public async Task Attention_CapsTheListButReportsTheTrueTotal()
    {
        var actions = Enumerable.Range(0, 520).Select(i => Action($"late {i:000}", new DateOnly(2026, 5, 1))).ToArray();

        var board = (await Service([], actions).GetAttentionBoardAsync(Tenant, null, default)).Value!;

        Assert.Equal(520, board.Total);
        Assert.Equal(500, board.Items.Count);
        Assert.True(board.Truncated);
    }

    // ------------------------------------------------------------------- risks

    [Fact]
    public async Task RiskOverview_NeedsTheRiskModule()
    {
        var result = await Service([Perf("A")]).GetRiskOverviewAsync(Tenant, null, default);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task RiskOverview_CountsApprovedRisksByLevelAndResponse_AndRanksTheOpenHighOnes()
    {
        var a = Perf("A");
        var b = Perf("B");
        var other = Perf("Z", status: "Completed");
        var risks = new FakeRisks(
            Risk(a.ProjectId, rpn: 20, plan: "plan", description: "a-high-planned"),
            Risk(a.ProjectId, rpn: 25, description: "a-high-unplanned"),
            Risk(a.ProjectId, level: "yellow", response: RiskResponseStatus.InProgress),
            Risk(b.ProjectId, level: "green", response: RiskResponseStatus.Completed),
            Risk(b.ProjectId, response: RiskResponseStatus.Completed, description: "b-high-handled"),
            Risk(a.ProjectId, approval: ApprovalStatus.PendingApproval, description: "not official"),
            Risk(other.ProjectId, description: "finished project"));

        var overview = (await Service([a, b, other], risks: risks).GetRiskOverviewAsync(Tenant, null, default)).Value!;

        Assert.Equal(5, overview.Total);
        Assert.Equal((3, 1, 1), (overview.High, overview.Medium, overview.Low));
        Assert.Equal(2, overview.OpenHigh);
        Assert.Equal(1, overview.OpenHighWithoutResponsePlan);
        Assert.Equal(["a-high-unplanned", "a-high-planned"], overview.TopRisks.Select(t => t.Description).ToArray()); // largest RPN first
        Assert.False(overview.TopRisks[0].HasResponsePlan);
        Assert.Equal(["Planning", "InProgress", "OnHold", "Completed"], overview.ByResponseStatus.Select(s => s.Name).ToArray());
        Assert.Equal([2, 1, 0, 2], overview.ByResponseStatus.Select(s => s.Count).ToArray());
        Assert.Equal(["A", "B"], overview.Projects.Select(p => p.Code).ToArray()); // most open high risks first
        Assert.Equal((3, 2, 1, 0, 2), (overview.Projects[0].Total, overview.Projects[0].High, overview.Projects[0].Medium, overview.Projects[0].Low, overview.Projects[0].OpenHigh));
    }

    [Fact]
    public async Task RiskOverview_KeepsOnlyTheTenTopRisks()
    {
        var project = Perf("A");
        var risks = new FakeRisks(Enumerable.Range(1, 14).Select(i => Risk(project.ProjectId, rpn: i, description: $"r{i}")).ToArray());

        var overview = (await Service([project], risks: risks).GetRiskOverviewAsync(Tenant, null, default)).Value!;

        Assert.Equal(14, overview.OpenHigh);
        Assert.Equal(10, overview.TopRisks.Count);
        Assert.Equal(14, overview.TopRisks[0].Rpn);
    }

    // -------------------------------------------------------------------- KPIs

    [Fact]
    public async Task KpiOverview_NeedsTheKpiModule()
    {
        var result = await Service([Perf("A")], kpis: new FakeKpis()).GetKpiOverviewAsync(Tenant, null, default); // no measurement repository

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task KpiOverview_ComparesTheLatestValueWithTheTarget_AndRatesIt()
    {
        var project = Perf("A");
        var excellent = KpiWithTarget(project.ProjectId, 100m, "excellent");
        var good = KpiWithTarget(project.ProjectId, 100m, "good");
        var poor = KpiWithTarget(project.ProjectId, 100m, "poor");
        var noTarget = KpiWithTarget(project.ProjectId, null, "no target");
        var noData = KpiWithTarget(project.ProjectId, 100m, "no data");
        var over = KpiWithTarget(project.ProjectId, 50m, "over");
        var measurements = new FakeMeasurements(
            Measured(excellent, new DateOnly(2026, 5, 1), 10m), Measured(excellent, new DateOnly(2026, 5, 20), 95m), // the latest counts
            Measured(good, new DateOnly(2026, 5, 20), 75m),
            Measured(poor, new DateOnly(2026, 5, 20), 40m),
            Measured(noTarget, new DateOnly(2026, 5, 20), 7m),
            Measured(over, new DateOnly(2026, 5, 20), 80m),
            Measured(excellent, new DateOnly(2026, 6, 20), 1m)); // after "today": not yet measured

        var overview = (await Service([project], kpis: new FakeKpis(excellent, good, poor, noTarget, noData, over), measurements: measurements)
            .GetKpiOverviewAsync(Tenant, null, default)).Value!;

        KpiStatusDto Of(string description) => overview.Kpis.Single(k => k.Description == description);
        Assert.Equal(95m, Of("excellent").AchievementPercent);
        Assert.Equal("Excellent", Of("excellent").Rating);
        Assert.Equal(2, Of("excellent").MeasurementCount); // the future value is not counted
        Assert.Equal("Good", Of("good").Rating);
        Assert.Equal("NeedsImprovement", Of("poor").Rating);
        Assert.Equal(160m, Of("over").AchievementPercent); // above target is reported as it is...
        Assert.Equal("Excellent", Of("over").Rating); // ...and rated as fully achieved
        Assert.Null(Of("no target").AchievementPercent);
        Assert.Null(Of("no target").Rating);
        Assert.Equal(7m, Of("no target").LatestValue);
        Assert.Null(Of("no data").LatestValue);
        Assert.Equal(0, Of("no data").MeasurementCount);

        Assert.Equal(6, overview.KpiCount);
        Assert.Equal((5, 1), (overview.WithValues, overview.WithoutValues));
        Assert.Equal((2, 1, 1), (overview.Excellent, overview.Good, overview.NeedsImprovement));
        Assert.Equal(77.5m, overview.AverageAchievement); // (95 + 75 + 40 + 100) / 4: each capped at 100, KPIs without a figure left out
    }

    [Fact]
    public async Task KpiOverview_TrendIsTheLastTwelveValuesOldestFirst_AndOnlyRunningProjectsAreCovered()
    {
        var running = Perf("A");
        var done = Perf("B", status: "Completed");
        var kpi = KpiWithTarget(running.ProjectId, 100m);
        var elsewhere = KpiWithTarget(done.ProjectId, 100m);
        var measurements = new FakeMeasurements(
            Enumerable.Range(1, 15).Select(day => Measured(kpi, new DateOnly(2026, 5, day), day)).Append(Measured(elsewhere, new DateOnly(2026, 5, 1), 1m)).ToArray());

        var overview = (await Service([running, done], kpis: new FakeKpis(kpi, elsewhere), measurements: measurements)
            .GetKpiOverviewAsync(Tenant, null, default)).Value!;

        var status = Assert.Single(overview.Kpis);
        Assert.Equal(15, status.MeasurementCount);
        Assert.Equal(12, status.Trend.Count);
        Assert.Equal(4m, status.Trend[0].Value);
        Assert.Equal(15m, status.Trend[^1].Value);
    }

    // ----------------------------------------- the all-projects read of Analytics

    [Fact]
    public async Task AnalyticsProjectsPerformance_GivesTheSameFigures_AsTheSingleProjectReport_AndSkipsArchivedOnes()
    {
        var active = NewProject("P1", ProjectStatus.Active);
        var archived = NewProject("P2", ProjectStatus.Archived);
        var service = new AnalyticsService(
            new FakeProjectRepository(active, archived), new FakeActions(),
            new FakeProgress(active.Id, planned: 50m, actual: 40m), timeProvider: new FixedTime(Today));

        var all = (await service.GetProjectsPerformanceAsync(Tenant, null, default)).Value!;
        var single = (await service.GetProjectPerformanceAsync(Tenant, active.Id, null, default)).Value!;

        var row = Assert.Single(all);
        Assert.Equal(single, row);
        Assert.Equal(0.8m, row.Spi);
    }

    private static Project NewProject(string code, ProjectStatus status)
    {
        var project = new Project(Guid.NewGuid(), Tenant, code, code, ProjectType.Waterfall, Manager, null);
        project.UpdateDetails(code, code, Manager, null, null, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1000m, null, null, null, null, null, null);
        project.ChangeStatus(status);
        return project;
    }

    private sealed class FakeProjectRepository(params Project[] projects) : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(projects.SingleOrDefault(p => p.Id == id));
        public Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken ct)
        {
            var items = projects.Where(p => p.TenantId == request.TenantId).ToList();
            return Task.FromResult(new PagedResult<Project>(items, request.PageNumber, request.PageSize, items.Count));
        }

        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(Project project, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeProgress(Guid projectId, decimal planned, decimal actual) : IProgressService
    {
        public Task<Result<IReadOnlyList<ProgressUpdateDto>>> ListByProjectAsync(Guid id, CancellationToken ct)
        {
            IReadOnlyList<ProgressUpdateDto> list = id != projectId
                ? []
                : [new ProgressUpdateDto(Guid.NewGuid(), Tenant, projectId, null, new DateOnly(2026, 5, 1), planned, actual, null, null, actual - planned,
                    PerformanceClassification.AtRisk, ApprovalStatus.Approved, null)];
            return Task.FromResult(Result.Success(list));
        }

        public Task<Result<ProgressUpdateDto>> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ProgressUpdateDto>> CreateAsync(CreateProgressUpdateRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ProgressUpdateDto>> UpdateAsync(Guid id, UpdateProgressUpdateRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ProgressUpdateDto>> SubmitForApprovalAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
}
