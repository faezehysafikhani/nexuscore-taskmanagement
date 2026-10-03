using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.Integrations.StrategyAlignment.Application;
using Nexus.Integrations.StrategyAlignment.Domain;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Domain;
using Nexus.Reporting.Application.Analytics;
using Nexus.StrategyManagement.Application;
using Nexus.StrategyManagement.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

public sealed class AnalyticsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 6, 1);

    // ---------------------------------------------------------- earned value

    [Fact]
    public void EarnedValue_WorksOutEveryFigure_FromBudgetProgressAndCost()
    {
        var ev = EarnedValueCalculator.Calculate(
            budget: 1000m, plannedPercent: 50m, actualPercent: 40m, actualCost: 500m,
            start: new DateOnly(2026, 1, 1), end: new DateOnly(2026, 4, 11), asOf: Today);

        Assert.Equal(500m, ev.PlannedValue);
        Assert.Equal(400m, ev.EarnedValueAmount);
        Assert.Equal(-100m, ev.ScheduleVariance);
        Assert.Equal(0.8m, ev.Spi);
        Assert.Equal(-100m, ev.CostVariance);
        Assert.Equal(0.8m, ev.Cpi);
        Assert.Equal(1250m, ev.EstimateAtCompletion);
        Assert.Equal(750m, ev.EstimateToComplete);
        Assert.Equal(-250m, ev.VarianceAtCompletion);
        // 100 planned days at 0.8 speed = 125 days: 25 days late.
        Assert.Equal(new DateOnly(2026, 5, 6), ev.ForecastEnd);
        Assert.Equal(25, ev.ForecastDelayDays);
        Assert.Equal(HealthStatus.Red, ev.Health);
        Assert.True(ev.IsOverdue);
    }

    [Fact]
    public void EarnedValue_LeavesWhatItCannotWorkOutNull_NeverZero()
    {
        var noBudget = EarnedValueCalculator.Calculate(null, 50m, 40m, null, null, null, Today);
        Assert.Equal(0.8m, noBudget.Spi);
        Assert.Null(noBudget.PlannedValue);
        Assert.Null(noBudget.EarnedValueAmount);
        Assert.Null(noBudget.Cpi);

        var nothingPlanned = EarnedValueCalculator.Calculate(1000m, 0m, 0m, 0m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Today);
        Assert.Null(nothingPlanned.Spi);
        Assert.Null(nothingPlanned.ForecastEnd);
        Assert.Equal(0m, nothingPlanned.PlannedValue);
        Assert.Equal(HealthStatus.Unknown, nothingPlanned.Health);

        var noCostYet = EarnedValueCalculator.Calculate(1000m, 50m, 50m, 0m, null, null, Today);
        Assert.Null(noCostYet.Cpi);
        Assert.Null(noCostYet.EstimateAtCompletion);
        Assert.Equal(HealthStatus.Green, noCostYet.Health);

        var noProgress = EarnedValueCalculator.Calculate(1000m, null, null, 100m, null, null, Today);
        Assert.Null(noProgress.PlannedValue);
        Assert.Null(noProgress.CostVariance);
    }

    [Fact]
    public void EarnedValue_AFinishedProject_HasNoForecast_AndIsNotOverdue()
    {
        var done = EarnedValueCalculator.Calculate(1000m, 100m, 100m, 900m, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), Today);

        Assert.Null(done.ForecastEnd);
        Assert.False(done.IsOverdue);
        Assert.Equal(1000m, done.EarnedValueAmount);
        Assert.Equal(1.11m, done.Cpi);
    }

    [Theory]
    [InlineData(1.2, HealthStatus.Green)]
    [InlineData(0.95, HealthStatus.Green)]
    [InlineData(0.949, HealthStatus.Amber)]
    [InlineData(0.85, HealthStatus.Amber)]
    [InlineData(0.849, HealthStatus.Red)]
    [InlineData(0, HealthStatus.Red)]
    public void Health_FollowsTheThresholds(double index, HealthStatus expected) =>
        Assert.Equal(expected, EarnedValueCalculator.Classify((decimal)index));

    [Fact]
    public void Health_UnknownWhenNoIndex_AndCombiningTakesTheWorse()
    {
        Assert.Equal(HealthStatus.Unknown, EarnedValueCalculator.Classify(null));
        Assert.Equal(HealthStatus.Red, EarnedValueCalculator.Combine(HealthStatus.Green, HealthStatus.Red));
        Assert.Equal(HealthStatus.Green, EarnedValueCalculator.Combine(HealthStatus.Unknown, HealthStatus.Green));
        Assert.Equal(HealthStatus.Unknown, EarnedValueCalculator.Combine(HealthStatus.Unknown, HealthStatus.Unknown));
    }

    // ----------------------------------------------------------------- fakes

    private sealed class FakeProjects(params Project[] projects) : IProjectRepository
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

    private sealed class FakeActions(params ActionItem[] actions) : IActionItemRepository
    {
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(actions.SingleOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ActionItem>>(actions);
        public Task AddAsync(ActionItem action, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeProgress(Dictionary<Guid, List<ProgressUpdateDto>> byProject) : IProgressService
    {
        public Task<Result<IReadOnlyList<ProgressUpdateDto>>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<ProgressUpdateDto>>(byProject.GetValueOrDefault(projectId) ?? []));
        public Task<Result<ProgressUpdateDto>> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ProgressUpdateDto>> CreateAsync(CreateProgressUpdateRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ProgressUpdateDto>> UpdateAsync(Guid id, UpdateProgressUpdateRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ProgressUpdateDto>> SubmitForApprovalAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeContracts(Guid projectId, int count, decimal contracted, decimal invoiced, decimal paid) : IContractService
    {
        public Task<Result<ProjectContractsSummaryDto>> GetProjectSummaryAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Result.Success(new ProjectContractsSummaryDto(
                id, id == projectId ? count : 0, ContractMapper.Money(contracted), ContractMapper.Money(invoiced), ContractMapper.Money(paid),
                ContractMapper.Money(contracted - invoiced), ContractMapper.Money(invoiced - paid), 0, 0, 0)));
        public Task<Result<IReadOnlyList<ContractDto>>> ListByProjectAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ContractDetailDto>> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ContractDto>> CreateAsync(CreateContractRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ContractDto>> UpdateAsync(Guid id, UpdateContractRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ContractDto>> ChangeStatusAsync(Guid id, ChangeContractStatusRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ContractDto>> SubmitForApprovalAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeUnits(params OrganizationUnit[] units) : IOrganizationUnitRepository
    {
        public Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(units.SingleOrDefault(u => u.Id == id));
        public Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrganizationUnit>>(units);
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(OrganizationUnit unit, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeStrategies(params Strategy[] strategies) : IStrategyRepository
    {
        public Task<Strategy?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(strategies.SingleOrDefault(s => s.Id == id));
        public Task<IReadOnlyList<Strategy>> ListAsync(Guid tenantId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Strategy>>(strategies);
        public Task AddAsync(Strategy strategy, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveAsync(Strategy strategy, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeAlignments(params ProjectStrategyAlignment[] alignments) : IAlignmentRepository
    {
        public Task<ProjectStrategyAlignment?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(alignments.SingleOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<ProjectStrategyAlignment>> ListAsync(Guid tenantId, Guid? projectId, Guid? strategyId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProjectStrategyAlignment>>(alignments.ToList());
        public Task AddAsync(ProjectStrategyAlignment alignment, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedTime(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    // --------------------------------------------------------------- builders

    private static Project NewProject(
        string code, Guid? unit = null, Guid? manager = null, ProjectStatus status = ProjectStatus.Active,
        decimal? cost = 1000m, DateOnly? start = null, DateOnly? end = null, Guid? tenant = null)
    {
        var project = new Project(Guid.NewGuid(), tenant ?? Tenant, code, code, ProjectType.Waterfall, manager, null);
        project.UpdateDetails(code, code, manager, null, unit, null, start ?? new DateOnly(2026, 1, 1), end ?? new DateOnly(2026, 12, 31),
            cost, null, null, null, null, null, null);
        project.ChangeStatus(status);
        return project;
    }

    private static ProgressUpdateDto Progress(Guid projectId, DateOnly date, decimal planned, decimal actual)
    {
        var deviation = actual - planned;
        var classification = deviation >= 0 ? PerformanceClassification.OnTrack : deviation > -10 ? PerformanceClassification.AtRisk : PerformanceClassification.Behind;
        return new ProgressUpdateDto(Guid.NewGuid(), Tenant, projectId, null, date, planned, actual, null, null, deviation, classification, ApprovalStatus.Approved, null);
    }

    private static OrganizationUnit Unit(string name, Guid? parent = null) => new(Guid.NewGuid(), Tenant, name, name, parent);

    private static Dictionary<Guid, List<ProgressUpdateDto>> Updates(params ProgressUpdateDto[] updates) =>
        updates.GroupBy(u => u.ProjectId).ToDictionary(g => g.Key, g => g.ToList());

    private static AnalyticsService Service(
        Project[] projects, Dictionary<Guid, List<ProgressUpdateDto>>? progress = null, IContractService? contracts = null,
        OrganizationUnit[]? units = null, Strategy[]? strategies = null, ProjectStrategyAlignment[]? alignments = null,
        ActionItem[]? actions = null) =>
        new(new FakeProjects(projects), new FakeActions(actions ?? []),
            progress is null ? null : new FakeProgress(progress),
            contracts,
            units is null ? null : new FakeUnits(units),
            strategies is null ? null : new FakeStrategies(strategies),
            alignments is null ? null : new FakeAlignments(alignments),
            new FixedTime(Today));

    // ------------------------------------------------------- project performance

    [Fact]
    public async Task ProjectPerformance_UsesTheLatestUpdateOnOrBeforeTheDate_AndTheInvoicedAmountAsActualCost()
    {
        var project = NewProject("P1", cost: 1000m, start: new DateOnly(2026, 1, 1), end: new DateOnly(2026, 4, 11));
        var progress = Updates(
            Progress(project.Id, new DateOnly(2026, 3, 1), 30, 30),
            Progress(project.Id, new DateOnly(2026, 5, 1), 50, 40),
            Progress(project.Id, new DateOnly(2026, 7, 1), 90, 90)); // after "today": not yet reported
        var service = Service([project], progress, new FakeContracts(project.Id, 2, 900m, 500m, 200m));

        var dto = (await service.GetProjectPerformanceAsync(Tenant, project.Id, null, default)).Value!;

        Assert.Equal(Today, dto.AsOf);
        Assert.Equal(40m, dto.ActualProgress);
        Assert.Equal(new DateOnly(2026, 5, 1), dto.ProgressDate);
        Assert.Equal(0.8m, dto.Spi);
        Assert.Equal(500m, dto.ActualCost);
        Assert.Equal(0.8m, dto.Cpi);
        Assert.Equal(900m, dto.ContractedAmount);
        Assert.Equal(200m, dto.PaidAmount);
        Assert.Equal(HealthStatus.Red, dto.Health);
        Assert.True(dto.ContractsAvailable);
        Assert.True(dto.ProgressAvailable);
    }

    [Fact]
    public async Task ProjectPerformance_ACustomDate_PicksThatDaysProgress()
    {
        var project = NewProject("P1");
        var progress = Updates(Progress(project.Id, new DateOnly(2026, 3, 1), 30, 30), Progress(project.Id, new DateOnly(2026, 5, 1), 50, 40));

        var dto = (await Service([project], progress).GetProjectPerformanceAsync(Tenant, project.Id, new DateOnly(2026, 4, 1), default)).Value!;

        Assert.Equal(30m, dto.ActualProgress);
        Assert.Equal(new DateOnly(2026, 4, 1), dto.AsOf);
    }

    [Fact]
    public async Task ProjectPerformance_WithoutProgressOrContracts_StillReturnsTheProject_WithNullFigures()
    {
        var project = NewProject("P1");

        var dto = (await Service([project]).GetProjectPerformanceAsync(Tenant, project.Id, null, default)).Value!;

        Assert.Null(dto.ActualProgress);
        Assert.Null(dto.Spi);
        Assert.Null(dto.ActualCost);
        Assert.False(dto.ContractsAvailable);
        Assert.False(dto.ProgressAvailable);
        Assert.Equal(HealthStatus.Unknown, dto.Health);
    }

    [Fact]
    public async Task ProjectPerformance_AProjectWithNoContracts_HasNoActualCost()
    {
        var project = NewProject("P1");
        var progress = Updates(Progress(project.Id, new DateOnly(2026, 5, 1), 50, 50));

        var dto = (await Service([project], progress, new FakeContracts(Guid.NewGuid(), 0, 0, 0, 0)).GetProjectPerformanceAsync(Tenant, project.Id, null, default)).Value!;

        Assert.Null(dto.ActualCost);
        Assert.Null(dto.Cpi);
        Assert.True(dto.ContractsAvailable);
        Assert.Equal(1m, dto.Spi);
    }

    [Fact]
    public async Task ProjectPerformance_AnotherTenantsProject_IsNotFound()
    {
        var project = NewProject("P1");

        var result = await Service([project]).GetProjectPerformanceAsync(Guid.NewGuid(), project.Id, null, default);

        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal("not_found", (await Service([project]).GetProjectPerformanceAsync(Tenant, Guid.NewGuid(), null, default)).Error.Code);
    }

    // --------------------------------------------------------------------- units

    private sealed record Org(OrganizationUnit Root, OrganizationUnit Child1, OrganizationUnit Child2, OrganizationUnit Grandchild)
    {
        public OrganizationUnit[] All => [Root, Child1, Child2, Grandchild];
    }

    private static Org BuildOrg()
    {
        var root = Unit("Head office");
        var child1 = Unit("Engineering", root.Id);
        var child2 = Unit("Finance", root.Id);
        var grandchild = Unit("Civil", child1.Id);
        return new Org(root, child1, child2, grandchild);
    }

    [Fact]
    public async Task UnitPerformance_RollsDeeperProjectsUpToTheLevel_AndKeepsShallowerOnesWhereTheyAre()
    {
        var org = BuildOrg();
        var deep = NewProject("DEEP", org.Grandchild.Id, Alice);
        var finance = NewProject("FIN", org.Child2.Id, Alice, cost: 500m);
        var top = NewProject("TOP", org.Root.Id, Bob);
        var none = NewProject("NONE", null, Bob, status: ProjectStatus.Completed);
        var archived = NewProject("OLD", org.Child1.Id, Bob, status: ProjectStatus.Archived);
        var progress = Updates(
            Progress(deep.Id, new DateOnly(2026, 5, 1), 60, 40),
            Progress(finance.Id, new DateOnly(2026, 5, 1), 50, 60));
        var service = Service([deep, finance, top, none, archived], progress, units: org.All);

        var report = (await service.GetUnitPerformanceAsync(Tenant, 2, null, default)).Value!;

        Assert.Equal(2, report.Level);
        Assert.Equal(3, report.MaxLevel);
        var byName = report.Units.ToDictionary(u => u.UnitName);
        // Level-2 units, the root that holds a project directly, and the unassigned bucket.
        Assert.Equal(["Engineering", "Finance", "Head office", "Unassigned"], report.Units.Select(u => u.UnitName).Order(StringComparer.Ordinal));

        var engineering = byName["Engineering"];
        Assert.Equal(1, engineering.ProjectCount); // DEEP rolled up from Civil; the archived one is left out
        Assert.Equal(40m, engineering.AverageActualProgress);
        Assert.Equal(-20m, engineering.AverageDeviation);
        Assert.Equal(1, engineering.Behind + engineering.AtRisk);

        Assert.Equal(500m, byName["Finance"].TotalBudget);
        Assert.Equal(1, byName["Finance"].OnTrack);
        Assert.Equal(1, byName["Head office"].ProjectCount);
        Assert.Equal(1, byName["Head office"].Level);
        Assert.Null(byName["Head office"].AverageActualProgress);

        var unassigned = byName["Unassigned"];
        Assert.Null(unassigned.UnitId);
        Assert.Equal(1, unassigned.CompletedProjects);
        Assert.Equal(0, unassigned.RunningProjects);
    }

    [Fact]
    public async Task UnitPerformance_LevelOne_CollapsesEverythingIntoTheRoots_AndAnEmptyUnitStillAppears()
    {
        var org = BuildOrg();
        var other = Unit("Other root");
        var service = Service([NewProject("A", org.Grandchild.Id), NewProject("B", org.Child2.Id)], units: [.. org.All, other]);

        var report = (await service.GetUnitPerformanceAsync(Tenant, 1, null, default)).Value!;

        Assert.Equal(2, report.Units.Count);
        Assert.Equal(2, report.Units.Single(u => u.UnitName == "Head office").ProjectCount);
        Assert.Equal(0, report.Units.Single(u => u.UnitName == "Other root").ProjectCount);
    }

    [Fact]
    public async Task UnitPerformance_ALevelBelowTheDeepestUnit_KeepsEachProjectWithItsOwnUnit()
    {
        var org = BuildOrg();
        var service = Service([NewProject("A", org.Child2.Id)], units: org.All);

        var report = (await service.GetUnitPerformanceAsync(Tenant, 9, null, default)).Value!;

        Assert.Equal("Finance", Assert.Single(report.Units, u => u.ProjectCount == 1).UnitName);
    }

    [Fact]
    public async Task UnitPerformance_AParentChainThatLoops_DoesNotHang()
    {
        var a = Unit("A");
        var b = Unit("B", a.Id);
        a.Update("A", "A", b.Id, null, true);
        var service = Service([NewProject("P", b.Id)], units: [a, b]);

        var report = (await service.GetUnitPerformanceAsync(Tenant, 5, null, default)).Value!;

        Assert.Equal(1, report.Units.Sum(u => u.ProjectCount));
    }

    [Fact]
    public async Task UnitPerformance_ProjectOverdueCount_OnlyCountsRunningUnfinishedProjects()
    {
        var org = BuildOrg();
        var late = NewProject("LATE", org.Child2.Id, end: new DateOnly(2026, 5, 1));
        var finishedLate = NewProject("DONE", org.Child2.Id, status: ProjectStatus.Completed, end: new DateOnly(2026, 5, 1));
        var future = NewProject("FUT", org.Child2.Id, end: new DateOnly(2026, 12, 1));
        var service = Service([late, finishedLate, future], units: org.All);

        var finance = (await service.GetUnitPerformanceAsync(Tenant, 2, null, default)).Value!.Units.Single(u => u.UnitName == "Finance");

        Assert.Equal(1, finance.OverdueProjects);
        Assert.Equal(2, finance.RunningProjects);
        Assert.Equal(1, finance.CompletedProjects);
    }

    [Fact]
    public async Task UnitReports_NeedAValidLevel_AndTheOrganizationModule()
    {
        var org = BuildOrg();
        Assert.Equal("validation.error", (await Service([], units: org.All).GetUnitPerformanceAsync(Tenant, 0, null, default)).Error.Code);
        Assert.Equal("validation.error", (await Service([], units: org.All).GetUnitStatusMatrixAsync(Tenant, -1, default)).Error.Code);
        Assert.Equal("conflict", (await Service([]).GetUnitPerformanceAsync(Tenant, 2, null, default)).Error.Code);
        Assert.Equal("conflict", (await Service([]).GetUnitStatusMatrixAsync(Tenant, 2, default)).Error.Code);
    }

    [Fact]
    public async Task UnitStatusMatrix_HasEveryStatusAsAColumn_WithRowAndColumnTotals()
    {
        var org = BuildOrg();
        var projects = new[]
        {
            NewProject("A", org.Grandchild.Id, status: ProjectStatus.Active),
            NewProject("B", org.Child1.Id, status: ProjectStatus.Completed),
            NewProject("C", org.Child2.Id, status: ProjectStatus.Active),
            NewProject("D", null, status: ProjectStatus.Draft),
            NewProject("E", org.Child2.Id, status: ProjectStatus.Archived)
        };

        var matrix = (await Service(projects, units: org.All).GetUnitStatusMatrixAsync(Tenant, 2, default)).Value!;

        Assert.Equal(["Draft", "Active", "OnHold", "Completed", "Archived"], matrix.Statuses);
        var engineering = matrix.Rows.Single(r => r.UnitName == "Engineering");
        Assert.Equal([0, 1, 0, 1, 0], engineering.Counts);
        Assert.Equal(2, engineering.Total);
        Assert.Equal([0, 1, 0, 0, 0], matrix.Rows.Single(r => r.UnitName == "Finance").Counts);
        Assert.Equal([1, 0, 0, 0, 0], matrix.Rows.Single(r => r.UnitId is null).Counts);
        Assert.Equal([1, 2, 0, 1, 0], matrix.ColumnTotals);
        Assert.Equal(4, matrix.Total);
    }

    // ------------------------------------------------------------ project managers

    [Fact]
    public async Task ManagerEvaluation_SummarisesEachManagersProjects_AndOpenActions()
    {
        var a1 = NewProject("A1", manager: Alice, cost: 100m);
        var a2 = NewProject("A2", manager: Alice, cost: 200m, end: new DateOnly(2026, 5, 1));
        var a3 = NewProject("A3", manager: Alice, status: ProjectStatus.Completed);
        var b1 = NewProject("B1", manager: Bob);
        var nobody = NewProject("N1");
        var progress = Updates(
            Progress(a1.Id, new DateOnly(2026, 5, 1), 50, 55),
            Progress(a2.Id, new DateOnly(2026, 5, 1), 80, 50),
            Progress(b1.Id, new DateOnly(2026, 5, 1), 40, 36));
        var open = NewAction(Alice, ActionStatus.InProgress);
        var closed = NewAction(Alice, ActionStatus.Completed);
        var bobs = NewAction(Bob, ActionStatus.Open);

        var report = (await Service([a1, a2, a3, b1, nobody], progress, actions: [open, closed, bobs]).GetProjectManagerEvaluationAsync(Tenant, null, default)).Value!;

        Assert.Equal(Today, report.AsOf);
        var alice = report.Managers.Single(m => m.ManagerUserId == Alice);
        Assert.Equal(3, alice.ProjectCount);
        Assert.Equal(2, alice.RunningProjects);
        Assert.Equal(1, alice.CompletedProjects);
        Assert.Equal(1, alice.OverdueProjects); // A2 ended on 1 May at 50%
        Assert.Equal(1300m, alice.TotalBudget); // 100 + 200 + A3's default 1000
        Assert.Equal(2, alice.ProjectsWithProgress);
        Assert.Equal(52.5m, alice.AverageActualProgress);
        Assert.Equal(65m, alice.AveragePlannedProgress);
        Assert.Equal(-12.5m, alice.AverageDeviation);
        Assert.Equal(1, alice.OnTrack);
        Assert.Equal(1, alice.Behind);
        Assert.Equal(50m, alice.OnTrackShare);
        Assert.Equal(1, alice.OpenActions);

        var bob = report.Managers.Single(m => m.ManagerUserId == Bob);
        Assert.Equal(1, bob.AtRisk);
        Assert.Equal(1, bob.OpenActions);

        var unassigned = report.Managers.Single(m => m.ManagerUserId is null);
        Assert.Equal(1, unassigned.ProjectCount);
        Assert.Null(unassigned.OnTrackShare);
        Assert.Null(unassigned.AverageDeviation);
        Assert.Equal(Alice, report.Managers[0].ManagerUserId); // most projects first
    }

    private static ActionItem NewAction(Guid responsible, ActionStatus status)
    {
        var action = new ActionItem(Guid.NewGuid(), Tenant, "Action", Guid.NewGuid(), Guid.NewGuid());
        action.UpdateDetails("Action", null, null, responsible, Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        action.ChangeStatus(status);
        return action;
    }

    // ------------------------------------------------------------------ alignment

    private static Strategy NewStrategy(string name, decimal weight = 0m, Guid? parent = null)
    {
        var strategy = new Strategy(Guid.NewGuid(), Tenant, name, parent);
        strategy.UpdateDetails(name, null, weight, parent);
        return strategy;
    }

    private static ProjectStrategyAlignment Link(Project project, Strategy strategy, AlignmentLevel level, decimal? percentage)
    {
        var alignment = new ProjectStrategyAlignment(Guid.NewGuid(), Tenant, project.Id, strategy.Id, level);
        alignment.Update(level, percentage);
        return alignment;
    }

    [Fact]
    public async Task AlignmentMatrix_IsCompleteAndPointsOutTheGaps()
    {
        var growth = NewStrategy("Growth", 2m);
        var export = NewStrategy("Export", 1m, growth.Id);
        var quality = NewStrategy("Quality", 1m);
        var p1 = NewProject("P1");
        var p2 = NewProject("P2");
        var p3 = NewProject("P3");
        var service = Service([p1, p2, p3], strategies: [growth, export, quality], alignments:
        [
            Link(p1, export, AlignmentLevel.High, 80m),
            Link(p1, quality, AlignmentLevel.Low, 20m),
            Link(p2, quality, AlignmentLevel.None, null),
            Link(p3, growth, AlignmentLevel.Medium, null),
            Link(p1, NewStrategy("Deleted"), AlignmentLevel.High, 90m) // link to a strategy that no longer exists
        ]);

        var matrix = (await service.GetStrategyAlignmentMatrixAsync(Tenant, default)).Value!;

        Assert.Equal(["Export", "Growth", "Quality"], matrix.Strategies.Select(s => s.Name));
        Assert.All(matrix.Projects, row => Assert.Equal(3, row.Cells.Count));

        var exportColumn = matrix.Strategies.Single(s => s.Name == "Export");
        Assert.Equal(2, exportColumn.Depth);
        Assert.Equal(1, exportColumn.DirectProjects);
        Assert.Equal(1, exportColumn.HighAlignedProjects);
        Assert.Equal(80m, exportColumn.AveragePercentage);

        // Growth has no direct link from P1, but its child Export does, and P3 links to it directly.
        var growthColumn = matrix.Strategies.Single(s => s.Name == "Growth");
        Assert.Equal(1, growthColumn.DirectProjects);
        Assert.Equal(2, growthColumn.ProjectsIncludingChildren);

        var row1 = matrix.Projects.Single(r => r.Code == "P1");
        Assert.Equal("High", row1.Cells[0].Level);
        Assert.Equal("None", row1.Cells[1].Level);
        Assert.Equal("Low", row1.Cells[2].Level);
        Assert.Equal(2, row1.AlignedStrategies);
        Assert.Equal(50m, row1.AveragePercentage);
        Assert.Equal(50m, row1.WeightedPercentage); // (1x80 + 1x20) / 2

        var row3 = matrix.Projects.Single(r => r.Code == "P3");
        Assert.Equal(1, row3.AlignedStrategies);
        Assert.Null(row3.AveragePercentage); // a level with no percentage is not guessed at

        // P2's only link is level None: it counts as unaligned.
        Assert.Equal([p2.Id], matrix.UnalignedProjectIds);
        Assert.Empty(matrix.UncoveredStrategyIds);
    }

    [Fact]
    public async Task AlignmentMatrix_ListsUncoveredStrategies_AndNeedsTheModules()
    {
        var covered = NewStrategy("Covered");
        var lonely = NewStrategy("Lonely");
        var p1 = NewProject("P1");
        var service = Service([p1], strategies: [covered, lonely], alignments: [Link(p1, covered, AlignmentLevel.High, 70m)]);

        var matrix = (await service.GetStrategyAlignmentMatrixAsync(Tenant, default)).Value!;

        Assert.Equal([lonely.Id], matrix.UncoveredStrategyIds);
        Assert.Empty(matrix.UnalignedProjectIds);
        Assert.Equal("conflict", (await Service([p1]).GetStrategyAlignmentMatrixAsync(Tenant, default)).Error.Code);
    }
}
