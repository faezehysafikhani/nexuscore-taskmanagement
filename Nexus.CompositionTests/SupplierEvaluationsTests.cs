using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>Periodic evaluations of a supplier: scores, the facts recorded with them, and what the supplier list shows of them.</summary>
public sealed class SupplierEvaluationsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 1);
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    private sealed class FixedClock(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private sealed class NotConfigured : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private sealed class Fixture
    {
        public ContractsDbContext Db { get; }
        public ContractService Contracts { get; }
        public ContractCommitmentService Commitments { get; }
        public SupplierService Suppliers { get; }
        public SupplierEvaluationService Evaluations { get; }

        public Fixture()
        {
            Db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var repository = new ContractRepository(Db);
            var supplierRepository = new SupplierRepository(Db);
            var commitmentRepository = new CommitmentRepository(Db);
            var clock = new FixedClock(Today);
            Contracts = new ContractService(repository, Db, new NotConfigured(), supplierRepository, commitmentRepository);
            Commitments = new ContractCommitmentService(repository, commitmentRepository, Db, clock);
            Suppliers = new SupplierService(supplierRepository, repository, Db);
            Evaluations = new SupplierEvaluationService(supplierRepository, commitmentRepository, Db, clock);
        }

        public async Task<ContractDto> ContractAsync(string number, string supplier) =>
            (await Contracts.CreateAsync(new CreateContractRequest(
                Tenant, Project, number, "Works " + number, supplier, null, From, From, To, 1_000_000m), default)).Value!;
    }

    private static SaveSupplierEvaluationRequest Scores(int q, int t, int c, int co, Guid? contractId = null, DateOnly? from = null, DateOnly? to = null) =>
        new(from ?? From, to ?? To, q, t, c, co, contractId, " fine ");

    [Fact]
    public async Task AnEvaluation_RecordsItsScoresAverage_AndTheFactsOfThePeriod()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync("1", "Acme");
        var supplierId = contract.SupplierId!.Value;
        // In the period: one milestone completed late, one on time, one still open past its due date; one obligation breached,
        // one open past its due date, one fulfilled. Outside the period: one late milestone that must not count.
        await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("Late done", new DateOnly(2026, 10, 5), Status: ContractMilestoneStatus.Completed, CompletedDate: new DateOnly(2026, 10, 9)), default);
        await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("On time", new DateOnly(2026, 10, 10), Status: ContractMilestoneStatus.Completed, CompletedDate: new DateOnly(2026, 10, 10)), default);
        await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("Still open", new DateOnly(2026, 10, 20)), default);
        await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("Before", new DateOnly(2026, 9, 1)), default);
        await f.Commitments.CreateObligationAsync(contract.Id, new SaveContractObligationRequest("Breached", DueDate: new DateOnly(2026, 10, 3), Status: ObligationStatus.Breached), default);
        await f.Commitments.CreateObligationAsync(contract.Id, new SaveContractObligationRequest("Open late", DueDate: new DateOnly(2026, 10, 15)), default);
        await f.Commitments.CreateObligationAsync(contract.Id, new SaveContractObligationRequest("Met", DueDate: new DateOnly(2026, 10, 15), Status: ObligationStatus.Fulfilled), default);

        var preview = (await f.Evaluations.GetMetricsAsync(Tenant, supplierId, From, To, null, default)).Value!;
        var saved = (await f.Evaluations.CreateAsync(Tenant, supplierId, Scores(4, 2, 3, 4), default)).Value!;

        Assert.Equal(new EvaluationMetricsDto(3, 2, 3, 2), preview);
        Assert.Equal(preview, saved.Metrics);
        Assert.Equal(3.25m, saved.OverallScore);
        Assert.Equal("fine", saved.Notes);
    }

    [Fact]
    public async Task OneContract_NarrowsTheFacts_AndMustBeTheSuppliers()
    {
        var f = new Fixture();
        var one = await f.ContractAsync("1", "Acme");
        var two = await f.ContractAsync("2", "Acme");
        var other = await f.ContractAsync("3", "Beta");
        await f.Commitments.CreateMilestoneAsync(one.Id, new SaveContractMilestoneRequest("A", new DateOnly(2026, 10, 5)), default);
        await f.Commitments.CreateMilestoneAsync(two.Id, new SaveContractMilestoneRequest("B", new DateOnly(2026, 10, 5)), default);
        var supplierId = one.SupplierId!.Value;

        var narrowed = (await f.Evaluations.CreateAsync(Tenant, supplierId, Scores(3, 3, 3, 3, one.Id), default)).Value!;
        var refused = await f.Evaluations.CreateAsync(Tenant, supplierId, Scores(3, 3, 3, 3, other.Id), default);

        Assert.Equal((1, 1), (narrowed.Metrics.MilestonesInPeriod, narrowed.Metrics.LateMilestones));
        Assert.Equal(one.Id, narrowed.ContractId);
        Assert.True(refused.IsFailure);
    }

    [Fact]
    public async Task ScoresOutOfRange_AReversedPeriod_AnotherTenantOrAnotherSupplier_AreRefused()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync("1", "Acme");
        var beta = await f.ContractAsync("2", "Beta");
        var supplierId = contract.SupplierId!.Value;
        var saved = (await f.Evaluations.CreateAsync(Tenant, supplierId, Scores(3, 3, 3, 3), default)).Value!;

        Assert.True((await f.Evaluations.CreateAsync(Tenant, supplierId, Scores(0, 3, 3, 3), default)).IsFailure);
        Assert.True((await f.Evaluations.CreateAsync(Tenant, supplierId, Scores(3, 6, 3, 3), default)).IsFailure);
        Assert.True((await f.Evaluations.CreateAsync(Tenant, supplierId, Scores(3, 3, 3, 3, from: To, to: From), default)).IsFailure);
        Assert.True((await f.Evaluations.CreateAsync(OtherTenant, supplierId, Scores(3, 3, 3, 3), default)).IsFailure);
        Assert.True((await f.Evaluations.ListAsync(OtherTenant, supplierId, default)).IsFailure);
        Assert.True((await f.Evaluations.UpdateAsync(Tenant, beta.SupplierId!.Value, saved.Id, Scores(3, 3, 3, 3), default)).IsFailure);
        Assert.True((await f.Evaluations.DeleteAsync(Tenant, beta.SupplierId!.Value, saved.Id, default)).IsFailure);
    }

    [Fact]
    public async Task AnUpdate_TakesTheFactsAgain()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync("1", "Acme");
        var supplierId = contract.SupplierId!.Value;
        var saved = (await f.Evaluations.CreateAsync(Tenant, supplierId, Scores(5, 5, 5, 5), default)).Value!;
        Assert.Equal(0, saved.Metrics.MilestonesInPeriod);

        await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("Added later", new DateOnly(2026, 10, 5)), default);
        var updated = (await f.Evaluations.UpdateAsync(Tenant, supplierId, saved.Id, Scores(2, 2, 2, 2), default)).Value!;

        Assert.Equal((1, 1), (updated.Metrics.MilestonesInPeriod, updated.Metrics.LateMilestones));
        Assert.Equal(2m, updated.OverallScore);
    }

    [Fact]
    public async Task TheSupplierList_ShowsTheLatestAndAverageScore_AndDeletingTheSupplierTakesItsEvaluations()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync("1", "Acme");
        var supplierId = contract.SupplierId!.Value;
        await f.Evaluations.CreateAsync(Tenant, supplierId, Scores(2, 2, 2, 2, from: new DateOnly(2026, 4, 1), to: new DateOnly(2026, 6, 30)), default);
        await f.Evaluations.CreateAsync(Tenant, supplierId, Scores(4, 4, 4, 5), default);

        var stats = (await f.Suppliers.ListAsync(Tenant, default)).Value!.Single().Stats;

        Assert.Equal(2, stats.EvaluationCount);
        Assert.Equal(4.25m, stats.LatestScore); // the one whose period ends last
        Assert.Equal(3.13m, stats.AverageScore); // (2 + 4.25) / 2, rounded half away from zero

        await f.Contracts.DeleteAsync(contract.Id, default);
        Assert.True((await f.Suppliers.DeleteAsync(Tenant, supplierId, default)).IsSuccess);
        Assert.Empty(await f.Db.SupplierEvaluations.ToListAsync());
    }

    // ---- the upgrade script and its code twin ----

    private const string ScriptFile = "2026-10-19-add-supplier-evaluations.sql";

    private static ContractsDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<ContractsDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(db, "project_contracts", ScriptFile, typeof(ContractsEvaluationsSchemaUpgrade), ["SupplierEvaluations"]);
    }

    [Fact]
    public void TheUpgradeScript_OnlyCreates()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly(ScriptFile, typeof(ContractsEvaluationsSchemaUpgrade));
    }
}
