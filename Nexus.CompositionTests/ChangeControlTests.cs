using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

/// <summary>Addenda that carry out a change request, and the project's change dashboard with the changes made without approval.</summary>
public sealed class ChangeControlTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 3);
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Requester(ApprovalRequestOutcome outcome) : IApprovalRequester
    {
        public ApprovalRequestOutcome Outcome { get; set; } = outcome;
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(Outcome);
    }

    /// <summary>Only what the dashboard reads: one project with a budget and planned dates.</summary>
    private sealed class OneProject(Project project) : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == project.Id ? project : null);
        public Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(Project project, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Fixture
    {
        public Project Project { get; }
        public Requester Approvals { get; } = new(ApprovalRequestOutcome.NotConfigured);
        public ContractService Contracts { get; }
        public ContractAddendumService Addenda { get; }
        public ChangeRequestService Changes { get; }
        public ChangeDashboardService Dashboard { get; }

        public Fixture()
        {
            Project = new Project(Guid.NewGuid(), Tenant, "Road", "P-1", ProjectType.Waterfall);
            Project.UpdateDetails("Road", "P-1", null, null, null, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 10_000_000_000m,
                null, null, null, null, null, null);
            var db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var contracts = new ContractRepository(db);
            var changes = new ChangeRequestRepository(db);
            Contracts = new ContractService(contracts, db, Approvals);
            Addenda = new ContractAddendumService(contracts, db, Approvals, changes);
            Changes = new ChangeRequestService(changes, contracts, db, Approvals, new FixedClock(Now.AddDays(-5)));
            Dashboard = new ChangeDashboardService(changes, contracts, new OneProject(Project), new FixedClock(Now));
        }

        public async Task<ContractDto> ActiveContractAsync(Guid? project = null)
        {
            var contract = (await Contracts.CreateAsync(new CreateContractRequest(
                Tenant, project ?? Project.Id, Guid.NewGuid().ToString("N")[..8], "Works", "Acme", null, Day, Day, Day.AddDays(300), 1_000_000_000m), default)).Value!;
            await Contracts.SubmitForApprovalAsync(contract.Id, default);
            return contract;
        }

        public async Task<ChangeRequestDto> ChangeAsync(string title, decimal cost, int days, bool approve, Guid? project = null)
        {
            var change = (await Changes.CreateAsync(new CreateChangeRequestRequest(
                Tenant, project ?? Project.Id, title, ScopeImpact: "Some scope", CostImpact: cost, ScheduleImpactDays: days), default)).Value!;
            return approve ? (await Changes.SubmitAsync(change.Id, default)).Value! : change;
        }

        public async Task<ContractAddendumDto> AddendumAsync(Guid contractId, decimal amount, int days, Guid? changeId, bool submit = true)
        {
            var addendum = (await Addenda.CreateAsync(contractId, new CreateContractAddendumRequest(
                Tenant, "Addendum", null, Day, amount, days, ChangeRequestId: changeId), default)).Value!;
            return submit ? (await Addenda.SubmitForApprovalAsync(contractId, addendum.Id, default)).Value! : addendum;
        }
    }

    [Fact]
    public async Task AnAddendum_CanNameOnlyAnApprovedChangeRequestOfItsProject()
    {
        var f = new Fixture();
        var contract = await f.ActiveContractAsync();
        var approved = await f.ChangeAsync("Approved", 100m, 1, approve: true);
        var draft = await f.ChangeAsync("Draft", 100m, 1, approve: false);
        var elsewhere = await f.ChangeAsync("Elsewhere", 100m, 1, approve: true, project: Guid.NewGuid());

        Assert.Equal(approved.Id, (await f.AddendumAsync(contract.Id, 100m, 0, approved.Id, submit: false)).ChangeRequestId);
        Assert.True((await f.Addenda.CreateAsync(contract.Id, new CreateContractAddendumRequest(Tenant, "x", null, Day, 1m, 0, ChangeRequestId: draft.Id), default)).IsFailure);
        Assert.True((await f.Addenda.CreateAsync(contract.Id, new CreateContractAddendumRequest(Tenant, "x", null, Day, 1m, 0, ChangeRequestId: elsewhere.Id), default)).IsFailure);
        Assert.True((await f.Addenda.CreateAsync(contract.Id, new CreateContractAddendumRequest(Tenant, "x", null, Day, 1m, 0, ChangeRequestId: Guid.NewGuid()), default)).IsFailure);
    }

    [Fact]
    public async Task AnUpdate_KeepsTheLinkWhenOmitted_AndTheEmptyIdRemovesIt()
    {
        var f = new Fixture();
        var contract = await f.ActiveContractAsync();
        var approved = await f.ChangeAsync("Approved", 100m, 1, approve: true);
        var addendum = await f.AddendumAsync(contract.Id, 100m, 0, approved.Id, submit: false);

        UpdateContractAddendumRequest Update(Guid? changeId) => new("Addendum", null, Day, 100m, 0, ChangeRequestId: changeId);

        Assert.Equal(approved.Id, (await f.Addenda.UpdateAsync(contract.Id, addendum.Id, Update(null), default)).Value!.ChangeRequestId);
        Assert.Null((await f.Addenda.UpdateAsync(contract.Id, addendum.Id, Update(Guid.Empty), default)).Value!.ChangeRequestId);
    }

    [Fact]
    public async Task TheDashboard_CountsTheChanges_AndWeighsTheApprovedImpactAgainstTheBaseline()
    {
        var f = new Fixture();
        await f.ChangeAsync("Approved", 1_000_000_000m, 30, approve: true);
        var implemented = await f.ChangeAsync("Implemented", 500_000_000m, 6, approve: true);
        await f.Changes.ImplementAsync(implemented.Id, new ImplementChangeRequestRequest(Day), default);
        await f.ChangeAsync("Draft", 9_000_000_000m, 99, approve: false);
        f.Approvals.Outcome = ApprovalRequestOutcome.Submitted;
        await f.ChangeAsync("Waiting", 0m, 0, approve: true);

        var d = (await f.Dashboard.GetAsync(f.Project.Id, default)).Value!;

        Assert.Equal((4, 1, 1, 1, 1), (d.Total, d.Drafts, d.Pending, d.Approved, d.Implemented));
        Assert.Equal((1_500_000_000m, 36), (d.ApprovedCostImpact.Rials, d.ApprovedScheduleImpactDays));
        Assert.Equal(15m, d.CostImpactPercent); // of a 10-billion budget
        Assert.Equal(364, d.PlannedDurationDays);
        Assert.Equal(9.89m, d.ScheduleImpactPercent); // 36 / 364
        Assert.NotNull(d.ImpactedEndDateFa);
        Assert.Equal(("Waiting", 5), (d.PendingChanges.Single().Title, d.PendingChanges.Single().WaitingDays));
    }

    [Fact]
    public async Task AddendaWithoutAnApprovedChangeRequest_AreListedAsChangesMadeWithoutApproval()
    {
        var f = new Fixture();
        var contract = await f.ActiveContractAsync();
        var approved = await f.ChangeAsync("Approved", 100m, 1, approve: true);
        var later = await f.ChangeAsync("Later cancelled", 100m, 1, approve: true);

        await f.AddendumAsync(contract.Id, 100_000_000m, 0, approved.Id);         // backed: not listed
        await f.AddendumAsync(contract.Id, 200_000_000m, 10, null);               // no change request
        await f.AddendumAsync(contract.Id, 50_000_000m, 0, later.Id);             // its change request is cancelled below
        await f.AddendumAsync(contract.Id, 999_000_000m, 0, null, submit: false); // a draft has not changed anything yet
        await f.Changes.CancelAsync(later.Id, new CancelChangeRequestRequest("dropped"), default);

        var d = (await f.Dashboard.GetAsync(f.Project.Id, default)).Value!;

        Assert.Equal(
            [(2, UnapprovedChangeReason.NoChangeRequest), (3, UnapprovedChangeReason.ChangeRequestNotApproved)],
            d.UnapprovedAddenda.Select(a => (a.Number, a.Reason)));
        Assert.Equal((250_000_000m, 10), (d.UnapprovedAmount.Rials, d.UnapprovedExtensionDays));
    }

    [Fact]
    public async Task WithoutABaseline_ThePercentagesAreLeftOut()
    {
        var f = new Fixture();
        var other = new ChangeDashboardService(new ChangeRequestRepository(new ContractsDbContext(
            new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)),
            new ContractRepository(new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)));

        var d = (await other.GetAsync(f.Project.Id, default)).Value!;

        Assert.Equal(0, d.Total);
        Assert.Null(d.Budget);
        Assert.Null(d.CostImpactPercent);
        Assert.Null(d.ScheduleImpactPercent);
        Assert.Empty(d.UnapprovedAddenda);
    }

    // ---- the upgrade script and its code twin ----

    private const string ScriptFile = "2026-10-19-add-addendum-change-request.sql";

    private static ContractsDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<ContractsDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "project_contracts", ScriptFile, typeof(ContractsAddendumChangeSchemaUpgrade), [],
            ("ContractAddenda", "[ChangeRequestId] uniqueidentifier NULL"));

        var script = SchemaUpgradeVerifier.Normalise(SchemaUpgradeVerifier.ReadScript(ScriptFile));
        Assert.Contains("CREATE INDEX [IX_ContractAddenda_ChangeRequestId] ON [project_contracts].[ContractAddenda] ([ChangeRequestId]);", db.Database.GenerateCreateScript());
        Assert.Contains("CREATE INDEX [IX_ContractAddenda_ChangeRequestId] ON [project_contracts].[ContractAddenda] ([ChangeRequestId]);", script);
    }

    [Fact]
    public void TheUpgradeScript_OnlyAdds()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly(ScriptFile, typeof(ContractsAddendumChangeSchemaUpgrade));
    }
}
