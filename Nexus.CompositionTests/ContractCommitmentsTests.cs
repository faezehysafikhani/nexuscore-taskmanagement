using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>The scope of work, milestones and obligations of a contract.</summary>
public sealed class ContractCommitmentsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 3);

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

        public Fixture(DateOnly? today = null)
        {
            Db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var repository = new ContractRepository(Db);
            var commitmentRepository = new CommitmentRepository(Db);
            Contracts = new ContractService(repository, Db, new NotConfigured(), new SupplierRepository(Db), commitmentRepository);
            Commitments = new ContractCommitmentService(repository, commitmentRepository, Db, new FixedClock(today ?? new DateOnly(2026, 11, 1)));
        }

        public async Task<ContractDto> ContractAsync(string number = "1", string? scope = null) =>
            (await Contracts.CreateAsync(new CreateContractRequest(
                Tenant, Project, number, "Road works", "Acme", null, Day, Day, Day.AddDays(100), 1_000_000_000m, ScopeOfWork: scope), default)).Value!;
    }

    [Fact]
    public async Task TheScopeOfWork_IsKept_AnUpdateThatOmitsItLeavesItAlone_AndAnEmptyTextClearsIt()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync(scope: "  Build 12 km of road  ");
        Assert.Equal("Build 12 km of road", contract.ScopeOfWork);

        UpdateContractRequest Update(string? scope) =>
            new("1", "Road works", "Acme", null, Day, Day, Day.AddDays(100), 1_000_000_000m, ScopeOfWork: scope);

        Assert.Equal("Build 12 km of road", (await f.Contracts.UpdateAsync(contract.Id, Update(null), default)).Value!.ScopeOfWork);
        Assert.Equal("Bridges too", (await f.Contracts.UpdateAsync(contract.Id, Update("Bridges too"), default)).Value!.ScopeOfWork);
        Assert.Null((await f.Contracts.UpdateAsync(contract.Id, Update(" "), default)).Value!.ScopeOfWork);
    }

    [Fact]
    public async Task AMilestone_IsOverdueOnlyWhilePastDueAndNotCompleted_AndKeepsItsCompletionDateOnlyWhenCompleted()
    {
        var f = new Fixture(today: new DateOnly(2026, 11, 1));
        var contract = await f.ContractAsync();

        var late = (await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("Foundations", new DateOnly(2026, 10, 15)), default)).Value!;
        var future = (await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("Roof", new DateOnly(2026, 12, 15)), default)).Value!;

        Assert.True(late.IsOverdue);
        Assert.False(future.IsOverdue);
        Assert.Equal("Foundations", (await f.Commitments.ListMilestonesAsync(contract.Id, default)).Value![0].Title); // by due date

        var done = (await f.Commitments.UpdateMilestoneAsync(contract.Id, late.Id,
            new SaveContractMilestoneRequest("Foundations", new DateOnly(2026, 10, 15), Status: ContractMilestoneStatus.Completed), default)).Value!;
        Assert.False(done.IsOverdue);
        Assert.Equal(new DateOnly(2026, 11, 1), done.CompletedDate); // defaults to today

        var reopened = (await f.Commitments.UpdateMilestoneAsync(contract.Id, late.Id,
            new SaveContractMilestoneRequest("Foundations", new DateOnly(2026, 10, 15), Status: ContractMilestoneStatus.InProgress, CompletedDate: Day), default)).Value!;
        Assert.Null(reopened.CompletedDate);
        Assert.True(reopened.IsOverdue);
    }

    [Fact]
    public async Task AnObligation_IsOverdueOnlyWhileOpenAndPastDue_AndHasNoDueDateRequired()
    {
        var f = new Fixture(today: new DateOnly(2026, 11, 1));
        var contract = await f.ContractAsync();

        var open = (await f.Commitments.CreateObligationAsync(contract.Id, new SaveContractObligationRequest("Insure the site", DueDate: new DateOnly(2026, 10, 20)), default)).Value!;
        var undated = (await f.Commitments.CreateObligationAsync(contract.Id, new SaveContractObligationRequest("Keep records", ObligationParty.Employer), default)).Value!;

        Assert.True(open.IsOverdue);
        Assert.False(undated.IsOverdue);
        Assert.Equal(ObligationParty.Employer, undated.Party);

        var met = (await f.Commitments.UpdateObligationAsync(contract.Id, open.Id,
            new SaveContractObligationRequest("Insure the site", DueDate: new DateOnly(2026, 10, 20), Status: ObligationStatus.Fulfilled, FulfilledDate: new DateOnly(2026, 10, 18)), default)).Value!;
        Assert.False(met.IsOverdue);
        Assert.Equal(new DateOnly(2026, 10, 18), met.FulfilledDate);

        var breached = (await f.Commitments.UpdateObligationAsync(contract.Id, open.Id,
            new SaveContractObligationRequest("Insure the site", DueDate: new DateOnly(2026, 10, 20), Status: ObligationStatus.Breached), default)).Value!;
        Assert.False(breached.IsOverdue);
        Assert.Null(breached.FulfilledDate);
    }

    [Fact]
    public async Task TheSummary_CountsEachKind()
    {
        var f = new Fixture(today: new DateOnly(2026, 11, 1));
        var contract = await f.ContractAsync();
        await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("A", new DateOnly(2026, 10, 1)), default);
        await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("B", new DateOnly(2026, 10, 2), Status: ContractMilestoneStatus.Completed), default);
        await f.Commitments.CreateObligationAsync(contract.Id, new SaveContractObligationRequest("X", DueDate: new DateOnly(2026, 10, 1)), default);
        await f.Commitments.CreateObligationAsync(contract.Id, new SaveContractObligationRequest("Y", Status: ObligationStatus.Breached), default);

        var s = (await f.Commitments.GetSummaryAsync(contract.Id, default)).Value!;

        Assert.Equal((2, 1, 1), (s.MilestoneCount, s.CompletedMilestones, s.OverdueMilestones));
        Assert.Equal((2, 0, 1, 1), (s.ObligationCount, s.FulfilledObligations, s.OverdueObligations, s.BreachedObligations));
    }

    [Fact]
    public async Task ARequest_ThatIsInvalidOrForAnotherContract_IsRefused()
    {
        var f = new Fixture();
        var one = await f.ContractAsync("1");
        var two = await f.ContractAsync("2");
        var milestone = (await f.Commitments.CreateMilestoneAsync(one.Id, new SaveContractMilestoneRequest("M", Day), default)).Value!;

        Assert.True((await f.Commitments.CreateMilestoneAsync(one.Id, new SaveContractMilestoneRequest("  ", Day), default)).IsFailure);
        Assert.True((await f.Commitments.CreateMilestoneAsync(one.Id, new SaveContractMilestoneRequest("M", Day, Status: (ContractMilestoneStatus)9), default)).IsFailure);
        Assert.True((await f.Commitments.CreateMilestoneAsync(Guid.NewGuid(), new SaveContractMilestoneRequest("M", Day), default)).IsFailure);
        Assert.True((await f.Commitments.UpdateMilestoneAsync(two.Id, milestone.Id, new SaveContractMilestoneRequest("M", Day), default)).IsFailure);
        Assert.True((await f.Commitments.DeleteMilestoneAsync(two.Id, milestone.Id, default)).IsFailure);
        Assert.True((await f.Commitments.CreateObligationAsync(one.Id, new SaveContractObligationRequest("", Day2()), default)).IsFailure);

        static ObligationParty Day2() => ObligationParty.Supplier;
    }

    [Fact]
    public async Task DeletingAContract_TakesItsMilestonesAndObligationsWithIt()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync();
        await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("M", Day), default);
        await f.Commitments.CreateObligationAsync(contract.Id, new SaveContractObligationRequest("O"), default);

        var deleted = await f.Contracts.DeleteAsync(contract.Id, default);

        Assert.True(deleted.IsSuccess);
        Assert.Empty(await f.Db.ContractMilestones.ToListAsync());
        Assert.Empty(await f.Db.ContractObligations.ToListAsync());
    }

    // ---- the upgrade script and its code twin ----

    private const string ScriptFile = "2026-10-19-add-contract-commitments.sql";

    private static ContractsDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<ContractsDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "project_contracts", ScriptFile, typeof(ContractsCommitmentsSchemaUpgrade), ["ContractMilestones", "ContractObligations"],
            ("Contracts", "[ScopeOfWork] nvarchar(4000) NULL"));
    }

    [Fact]
    public void TheUpgradeScript_OnlyCreatesAndAdds()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly(ScriptFile, typeof(ContractsCommitmentsSchemaUpgrade));
    }
}
