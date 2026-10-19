using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using Nexus.Portfolio.Application;
using Nexus.Portfolio.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Domain;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.Reporting.Analytics;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

/// <summary>
/// Cost against progress, beyond one project's figures: the cost S-curve, the cost columns of the unit and
/// project-manager reports, and who may read one project's figures without Reporting.ViewAll.
/// </summary>
public sealed class CostAnalyticsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 6, 1);

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

    private sealed class NoActions : IActionItemRepository
    {
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<ActionItem?>(null);
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ActionItem>>([]);
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

    private sealed class FakeUnits(params OrganizationUnit[] units) : IOrganizationUnitRepository
    {
        public Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(units.SingleOrDefault(u => u.Id == id));
        public Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrganizationUnit>>(units);
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(OrganizationUnit unit, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Contracts: one detail (contract with its invoices) per project, as the Core would return them.</summary>
    private sealed class FakeContracts(params ContractDetailDto[] details) : IContractService
    {
        public Task<Result<IReadOnlyList<ContractDetailDto>>> ListForProjectsAsync(ListContractsForProjectsRequest request, CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<ContractDetailDto>>(details.Where(d => request.ProjectIds.Contains(d.Contract.ProjectId)).ToList()));
        public Task<Result<ProjectContractsSummaryDto>> GetProjectSummaryAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<ContractDto>>> ListByProjectAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ContractDetailDto>> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ContractDto>> CreateAsync(CreateContractRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ContractDto>> UpdateAsync(Guid id, UpdateContractRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ContractDto>> ChangeStatusAsync(Guid id, ChangeContractStatusRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ContractDto>> SubmitForApprovalAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeCurve(Result<SCurveDto> result) : IProgressCurveService
    {
        public Task<Result<SCurveDto>> GetSCurveAsync(Guid projectId, int stepDays, CancellationToken ct)
        {
            LastStepDays = stepDays;
            return Task.FromResult(result);
        }

        public int? LastStepDays { get; private set; }
        public Task<Result<IReadOnlyList<ProgressSnapshotDto>>> ListSnapshotsAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<ProgressSnapshotDto>> CreateSnapshotAsync(CreateProgressSnapshotRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result> DeleteSnapshotAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakePortfolio(Result<IReadOnlyList<ProjectDto>> visible) : IPortfolioService
    {
        public PortfolioQuery? Asked { get; private set; }
        public Task<Result<PortfolioResultDto>> GetPortfolioAsync(PortfolioQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<ProjectDto>>> GetVisibleProjectsAsync(PortfolioQuery query, bool includeArchived, CancellationToken ct)
        {
            Asked = query;
            return Task.FromResult(visible);
        }
    }

    private sealed class PortfolioThatCannotList : IPortfolioService
    {
        public Task<Result<PortfolioResultDto>> GetPortfolioAsync(PortfolioQuery query, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FixedTime(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    // --------------------------------------------------------------- builders

    private static Project NewProject(string code, decimal? cost = 1000m, Guid? unit = null, Guid? manager = null, Guid? tenant = null)
    {
        var project = new Project(Guid.NewGuid(), tenant ?? Tenant, code, code, ProjectType.Waterfall, manager, null);
        project.UpdateDetails(code, code, manager, null, unit, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            cost, null, null, null, null, null, null);
        project.ChangeStatus(ProjectStatus.Active);
        return project;
    }

    private static ProjectDto Dto(Project project) => new(
        project.Id, project.TenantId, project.Name, project.Code, project.Type, project.Status, project.ApprovalStatus,
        null, project.ManagerUserId, project.OrganizationUnitId, null, project.StartDate, project.EndDate, project.Cost,
        null, null, null, null, null, null, DateTimeOffset.UtcNow);

    private static ProgressUpdateDto Progress(Guid projectId, decimal planned, decimal actual) =>
        new(Guid.NewGuid(), Tenant, projectId, null, new DateOnly(2026, 5, 1), planned, actual, null, null, actual - planned,
            actual >= planned ? PerformanceClassification.OnTrack : PerformanceClassification.AtRisk, ApprovalStatus.Approved, null);

    private static ContractInvoiceDto Invoice(Guid contractId, DateOnly date, decimal amount, ApprovalStatus approval) =>
        new(Guid.NewGuid(), contractId, "1", "1", date, "", ContractMapper.Money(amount), null, ContractMapper.Money(0), ContractMapper.Money(amount),
            null, null, approval, null);

    /// <summary>A contract of the project whose summary says "invoiced" (approved invoices only, as the Core works it out).</summary>
    private static ContractDetailDto Contract(Guid projectId, decimal invoicedSummary, params ContractInvoiceDto[] invoices)
    {
        var id = Guid.NewGuid();
        var summary = new ContractSummaryDto(
            ContractMapper.Money(1000), null, null, 0, 0, 0, ContractMapper.Money(invoicedSummary), 0, ContractMapper.Money(0),
            ContractMapper.Money(1000 - invoicedSummary), ContractMapper.Money(invoicedSummary), 0, 0, false);
        var contract = new ContractDto(
            id, Tenant, projectId, "C-1", "C-1", "Contract", "Party", null, null, null, null, null, null, null,
            ContractMapper.Money(1000), ContractStatus.Active, ApprovalStatus.Approved, summary, null);
        return new ContractDetailDto(contract, [], invoices.Select(i => i with { ContractId = id }).ToList());
    }

    private static SCurveDto Curve(Guid projectId, decimal currentPlanned = 50m, decimal currentActual = 40m) => new(
        projectId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 14, Today,
        [new CurvePointDto(new DateOnly(2026, 1, 1), 0m), new CurvePointDto(new DateOnly(2026, 6, 30), 50m), new CurvePointDto(new DateOnly(2026, 12, 31), 100m)],
        [new ActualPointDto(new DateOnly(2026, 3, 1), 20m, 25m), new ActualPointDto(new DateOnly(2026, 5, 1), 40m, 45m)],
        currentPlanned, currentActual, currentActual - currentPlanned, 0.8m);

    private static AnalyticsService Service(
        Project[] projects, IContractService? contracts = null, IProgressCurveService? curve = null,
        Dictionary<Guid, List<ProgressUpdateDto>>? progress = null, OrganizationUnit[]? units = null) =>
        new(new FakeProjects(projects), new NoActions(),
            progress is null ? null : new FakeProgress(progress),
            contracts,
            units is null ? null : new FakeUnits(units),
            null, null, new FixedTime(Today), null, null, curve);

    // ------------------------------------------------------------ cost curve

    [Fact]
    public async Task CostCurve_PricesThePlannedAndRecordedProgressWithTheBudget_AndSumsApprovedInvoicesOverTime()
    {
        var project = NewProject("P1", cost: 1000m);
        var contracts = new FakeContracts(Contract(project.Id, 300,
            Invoice(Guid.Empty, new DateOnly(2026, 3, 10), 100, ApprovalStatus.Approved),
            Invoice(Guid.Empty, new DateOnly(2026, 3, 10), 50, ApprovalStatus.Approved),
            Invoice(Guid.Empty, new DateOnly(2026, 5, 20), 150, ApprovalStatus.Approved),
            Invoice(Guid.Empty, new DateOnly(2026, 5, 25), 999, ApprovalStatus.PendingApproval),
            Invoice(Guid.Empty, new DateOnly(2026, 7, 1), 70, ApprovalStatus.Approved)));
        var service = Service([project], contracts, new FakeCurve(Result.Success(Curve(project.Id))));

        var result = await service.GetProjectCostCurveAsync(Tenant, project.Id, null, default);

        Assert.True(result.IsSuccess);
        var curve = result.Value!;
        Assert.Equal(1000m, curve.BudgetAtCompletion);
        Assert.Equal([0m, 500m, 1000m], curve.PlannedValue.Select(p => p.Amount));
        Assert.Equal([200m, 400m], curve.EarnedValue.Select(p => p.Amount));
        // one point per invoice day, a running total, and a pending invoice is no cost yet
        Assert.Equal([(new DateOnly(2026, 3, 10), 150m), (new DateOnly(2026, 5, 20), 300m), (new DateOnly(2026, 7, 1), 370m)],
            curve.ActualCost.Select(p => (p.Date, p.Amount)));
        Assert.Equal(500m, curve.CurrentPlannedValue);
        Assert.Equal(400m, curve.CurrentEarnedValue);
        Assert.Equal(300m, curve.CurrentActualCost); // as of Today (1 June): the July invoice is not yet incurred
        Assert.True(curve.ContractsAvailable);
    }

    [Fact]
    public async Task CostCurve_AsksTheScheduleForTheRequestedStep_OrAFortnight()
    {
        var project = NewProject("P1");
        var fake = new FakeCurve(Result.Success(Curve(project.Id)));
        var service = Service([project], null, fake);

        await service.GetProjectCostCurveAsync(Tenant, project.Id, null, default);
        Assert.Equal(14, fake.LastStepDays);

        await service.GetProjectCostCurveAsync(Tenant, project.Id, 7, default);
        Assert.Equal(7, fake.LastStepDays);
    }

    [Fact]
    public async Task CostCurve_WithoutABudget_HasNoPricedCurves_ButStillTheActualCost()
    {
        var project = NewProject("P1", cost: null);
        var contracts = new FakeContracts(Contract(project.Id, 100, Invoice(Guid.Empty, new DateOnly(2026, 3, 10), 100, ApprovalStatus.Approved)));
        var service = Service([project], contracts, new FakeCurve(Result.Success(Curve(project.Id))));

        var curve = (await service.GetProjectCostCurveAsync(Tenant, project.Id, null, default)).Value!;

        Assert.Null(curve.BudgetAtCompletion);
        Assert.Empty(curve.PlannedValue);
        Assert.Empty(curve.EarnedValue);
        Assert.Null(curve.CurrentPlannedValue);
        Assert.Null(curve.CurrentEarnedValue);
        Assert.Single(curve.ActualCost);
    }

    [Fact]
    public async Task CostCurve_WithoutContracts_SaysSo_InsteadOfReportingZeroCost()
    {
        var project = NewProject("P1");
        var service = Service([project], null, new FakeCurve(Result.Success(Curve(project.Id))));

        var curve = (await service.GetProjectCostCurveAsync(Tenant, project.Id, null, default)).Value!;

        Assert.False(curve.ContractsAvailable);
        Assert.Empty(curve.ActualCost);
        Assert.Equal(0m, curve.CurrentActualCost);
        Assert.Equal(3, curve.PlannedValue.Count);
    }

    [Fact]
    public async Task CostCurve_NeedsTheSchedule_AndKnowsTheProject()
    {
        var project = NewProject("P1");

        var noSchedule = await Service([project]).GetProjectCostCurveAsync(Tenant, project.Id, null, default);
        Assert.True(noSchedule.IsFailure);
        Assert.Contains("not installed", noSchedule.Error.Message);

        var failing = Service([project], null, new FakeCurve(Result.Failure<SCurveDto>(Error.Conflict("The project has no schedule."))));
        var noPlan = await failing.GetProjectCostCurveAsync(Tenant, project.Id, null, default);
        Assert.Equal("The project has no schedule.", noPlan.Error.Message);

        var good = Service([project], null, new FakeCurve(Result.Success(Curve(project.Id))));
        Assert.True((await good.GetProjectCostCurveAsync(Tenant, Guid.NewGuid(), null, default)).IsFailure);
        Assert.True((await good.GetProjectCostCurveAsync(Guid.NewGuid(), project.Id, null, default)).IsFailure); // another tenant's project does not exist for this caller
    }

    // ------------------------------------------------- unit and manager reports

    [Fact]
    public async Task UnitReport_AddsActualCost_EarnedValue_AndCpi_FromTheProjectsThatHaveBudgetProgressAndInvoices()
    {
        var unit = new OrganizationUnit(Guid.NewGuid(), Tenant, "Unit", "U", null);
        var p1 = NewProject("P1", cost: 1000m, unit: unit.Id);
        var p2 = NewProject("P2", cost: 1000m, unit: unit.Id);
        var noContract = NewProject("P3", cost: 1000m, unit: unit.Id);
        var progress = new Dictionary<Guid, List<ProgressUpdateDto>>
        {
            [p1.Id] = [Progress(p1.Id, 50, 50)],
            [p2.Id] = [Progress(p2.Id, 50, 20)],
            [noContract.Id] = [Progress(noContract.Id, 50, 90)],
        };
        var contracts = new FakeContracts(Contract(p1.Id, 400), Contract(p2.Id, 100));
        var service = Service([p1, p2, noContract], contracts, null, progress, [unit]);

        var report = (await service.GetUnitPerformanceAsync(Tenant, 1, null, default)).Value!;

        var row = Assert.Single(report.Units);
        Assert.Equal(500m, row.ActualCost);          // 400 + 100; the project without a contract has no known cost, not a zero one
        Assert.Equal(700m, row.EarnedValue);         // 1000 x 50% + 1000 x 20%
        Assert.Equal(1.4m, row.Cpi);                 // 700 / 500
        Assert.Equal(3000m, row.TotalBudget);             // the existing budget column is untouched
    }

    [Fact]
    public async Task UnitReport_LeavesTheCostColumnsNull_WhenContractsIsNotInstalled_OrNothingIsInvoiced()
    {
        var unit = new OrganizationUnit(Guid.NewGuid(), Tenant, "Unit", "U", null);
        var project = NewProject("P1", cost: 1000m, unit: unit.Id);
        var progress = new Dictionary<Guid, List<ProgressUpdateDto>> { [project.Id] = [Progress(project.Id, 50, 50)] };

        var without = (await Service([project], null, null, progress, [unit]).GetUnitPerformanceAsync(Tenant, 1, null, default)).Value!.Units.Single();
        Assert.Null(without.ActualCost);
        Assert.Null(without.EarnedValue);
        Assert.Null(without.Cpi);

        var nothingInvoiced = (await Service([project], new FakeContracts(Contract(project.Id, 0)), null, progress, [unit]).GetUnitPerformanceAsync(Tenant, 1, null, default)).Value!.Units.Single();
        Assert.Equal(0m, nothingInvoiced.ActualCost);
        Assert.Equal(500m, nothingInvoiced.EarnedValue);
        Assert.Null(nothingInvoiced.Cpi); // nothing spent yet: no index, never a made-up one
    }

    [Fact]
    public async Task ManagerReport_CarriesTheSameCostColumns()
    {
        var p1 = NewProject("P1", cost: 1000m, manager: Alice);
        var p2 = NewProject("P2", cost: 2000m, manager: Alice);
        var progress = new Dictionary<Guid, List<ProgressUpdateDto>> { [p1.Id] = [Progress(p1.Id, 50, 50)], [p2.Id] = [Progress(p2.Id, 50, 25)] };
        var service = Service([p1, p2], new FakeContracts(Contract(p1.Id, 250), Contract(p2.Id, 750)), null, progress);

        var manager = (await service.GetProjectManagerEvaluationAsync(Tenant, null, default)).Value!.Managers.Single();

        Assert.Equal(1000m, manager.ActualCost);
        Assert.Equal(1000m, manager.EarnedValue);   // 1000 x 50% + 2000 x 25%
        Assert.Equal(1m, manager.Cpi);
    }

    // ---------------------------------------------- who may read one project

    [Fact]
    public async Task ProjectAccess_FollowsThePortfoliosRule_ForTheCallerOnly()
    {
        var mine = NewProject("MINE");
        var other = NewProject("OTHER");
        var portfolio = new FakePortfolio(Result.Success<IReadOnlyList<ProjectDto>>([Dto(mine)]));
        var access = new PortfolioProjectViewAccess(portfolio);

        Assert.True(await access.CanViewAsync(Tenant, Alice, mine.Id, default));
        Assert.False(await access.CanViewAsync(Tenant, Alice, other.Id, default));

        // asked as a plain user - never as ViewAll - so the portfolio applies owner / manager / team / unit / approver visibility
        Assert.NotNull(portfolio.Asked);
        Assert.False(portfolio.Asked!.ViewAll);
        Assert.Equal(Alice, portfolio.Asked.CurrentUserId);
        Assert.Equal(Tenant, portfolio.Asked.TenantId);
    }

    [Fact]
    public async Task ProjectAccess_IsNo_WhenThereIsNoPortfolio_ItFails_OrItCannotListProjects()
    {
        var project = NewProject("P1");

        Assert.False(await new PortfolioProjectViewAccess(null).CanViewAsync(Tenant, Alice, project.Id, default));
        Assert.False(await new PortfolioProjectViewAccess(new FakePortfolio(Result.Failure<IReadOnlyList<ProjectDto>>(Error.Conflict("down"))))
            .CanViewAsync(Tenant, Alice, project.Id, default));
        Assert.False(await new PortfolioProjectViewAccess(new PortfolioThatCannotList()).CanViewAsync(Tenant, Alice, project.Id, default));
    }

    [Fact]
    public async Task ProjectAccess_NeverCrossesTenants()
    {
        var foreign = NewProject("F1", tenant: Guid.NewGuid());
        var portfolio = new FakePortfolio(Result.Success<IReadOnlyList<ProjectDto>>([Dto(foreign)]));

        Assert.False(await new PortfolioProjectViewAccess(portfolio).CanViewAsync(Tenant, Alice, foreign.Id, default));
    }
}
