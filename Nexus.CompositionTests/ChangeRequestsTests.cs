using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Application.EventHandlers;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>Change requests of a project: impact first, then a decision, then carrying it out or cancelling it.</summary>
public sealed class ChangeRequestsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid OtherProject = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 3);

    private sealed class Requester(ApprovalRequestOutcome outcome) : IApprovalRequester
    {
        public List<ApprovalSubject> Subjects { get; } = [];

        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct)
        {
            Subjects.Add(subject);
            return Task.FromResult(outcome);
        }
    }

    private sealed class Fixture
    {
        public ContractsDbContext Db { get; }
        public ChangeRequestRepository Repository { get; }
        public ContractService Contracts { get; }
        public ChangeRequestService Changes { get; }
        public Requester Approvals { get; }

        public Fixture(ApprovalRequestOutcome outcome = ApprovalRequestOutcome.NotConfigured)
        {
            Db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var contracts = new ContractRepository(Db);
            Repository = new ChangeRequestRepository(Db);
            Approvals = new Requester(outcome);
            Contracts = new ContractService(contracts, Db, new Requester(ApprovalRequestOutcome.NotConfigured));
            Changes = new ChangeRequestService(Repository, contracts, Db, Approvals);
        }

        public async Task<ChangeRequestDto> DraftAsync(string title = "Add a bridge", string? scopeImpact = "One more bridge in the scope", Guid? project = null) =>
            (await Changes.CreateAsync(new CreateChangeRequestRequest(
                Tenant, project ?? Project, title, ChangeRequestType.Scope, ChangeRequestPriority.High, "Why", "Client asked", "Client",
                null, scopeImpact, 30, 2_000_000_000m, "More risk in the rainy season"), default)).Value!;
    }

    [Fact]
    public async Task ADraft_IsNumberedPerProject_AndKeepsItsImpact()
    {
        var f = new Fixture();

        var first = await f.DraftAsync();
        var second = await f.DraftAsync("Second");
        var elsewhere = await f.DraftAsync("Elsewhere", project: OtherProject);

        Assert.Equal((1, "CR-001", 2, 1), (first.Number, first.Code, second.Number, elsewhere.Number));
        Assert.Equal(ChangeRequestStatus.Draft, first.Status);
        Assert.Equal((30, 2_000_000_000m, true), (first.ScheduleImpactDays, first.CostImpact.Rials, first.HasImpactAssessment));
        Assert.Equal(["Second", "Add a bridge"], (await f.Changes.ListAsync(Project, default)).Value!.Select(c => c.Title)); // newest first
    }

    [Fact]
    public async Task SubmittingNeedsTheImpactOnScope_AndWithoutWorkflowIsApprovedAtOnce()
    {
        var f = new Fixture();
        var bare = await f.DraftAsync("No impact", scopeImpact: "  ");
        var assessed = await f.DraftAsync();

        var refused = await f.Changes.SubmitAsync(bare.Id, default);
        var approved = (await f.Changes.SubmitAsync(assessed.Id, default)).Value!;

        Assert.True(refused.IsFailure);
        Assert.Equal(ChangeRequestStatus.Approved, approved.Status);
        Assert.NotNull(approved.SubmittedAtUtc);
        Assert.NotNull(approved.DecidedAtUtc);
        Assert.Equal(("ChangeRequest", "Project", Project), (f.Approvals.Subjects[0].SubjectType, f.Approvals.Subjects[0].ScopeType, f.Approvals.Subjects[0].ScopeId));
    }

    [Fact]
    public async Task WithWorkflow_ItWaits_ThenTheDecisionIsApplied_AndARejectedOneCanBeReworked()
    {
        var f = new Fixture(ApprovalRequestOutcome.Submitted);
        var one = await f.DraftAsync("One");
        var two = await f.DraftAsync("Two");
        var granted = new ContractApprovalGrantedHandler(new ContractRepository(f.Db), f.Db, f.Repository);
        var rejected = new ContractApprovalRejectedHandler(new ContractRepository(f.Db), f.Db, f.Repository);

        Assert.Equal(ChangeRequestStatus.PendingApproval, (await f.Changes.SubmitAsync(one.Id, default)).Value!.Status);
        await f.Changes.SubmitAsync(two.Id, default);
        Assert.True((await f.Changes.UpdateAsync(one.Id, new SaveChangeRequestRequest("Edited while pending"), default)).IsFailure);
        Assert.True((await f.Changes.CancelAsync(one.Id, new CancelChangeRequestRequest(), default)).IsFailure);

        await granted.HandleAsync(new ApprovalGranted(ChangeRequestService.SubjectType, one.Id, Tenant, null, " ok "), default);
        await rejected.HandleAsync(new ApprovalRejected(ChangeRequestService.SubjectType, two.Id, Tenant, null, "too costly"), default);

        Assert.Equal((ChangeRequestStatus.Approved, "ok"), ((await f.Changes.GetAsync(one.Id, default)).Value!.Status, (await f.Changes.GetAsync(one.Id, default)).Value!.DecisionNote));
        Assert.Equal((ChangeRequestStatus.Rejected, "too costly"), ((await f.Changes.GetAsync(two.Id, default)).Value!.Status, (await f.Changes.GetAsync(two.Id, default)).Value!.DecisionNote));

        var reworked = (await f.Changes.UpdateAsync(two.Id, new SaveChangeRequestRequest("Two, smaller", ScopeImpact: "Half a bridge"), default)).Value!;
        Assert.Equal((ChangeRequestStatus.Draft, (DateTimeOffset?)null, (string?)null), (reworked.Status, reworked.DecidedAtUtc, reworked.DecisionNote));
    }

    [Fact]
    public async Task OnlyAnApprovedOne_IsImplemented_AndCancellingAndDeletingFollowTheStatus()
    {
        var f = new Fixture();
        var draft = await f.DraftAsync("Draft");
        var approved = await f.DraftAsync("Approved");
        await f.Changes.SubmitAsync(approved.Id, default);

        Assert.True((await f.Changes.ImplementAsync(draft.Id, new ImplementChangeRequestRequest(), default)).IsFailure);
        var done = (await f.Changes.ImplementAsync(approved.Id, new ImplementChangeRequestRequest(Day, " built "), default)).Value!;
        Assert.Equal((ChangeRequestStatus.Implemented, Day, "built"), (done.Status, done.ImplementedDate, done.OutcomeNote));
        Assert.True((await f.Changes.CancelAsync(approved.Id, new CancelChangeRequestRequest(), default)).IsFailure);
        Assert.True((await f.Changes.DeleteAsync(approved.Id, default)).IsFailure);

        var cancelled = (await f.Changes.CancelAsync(draft.Id, new CancelChangeRequestRequest("not needed"), default)).Value!;
        Assert.Equal(ChangeRequestStatus.Cancelled, cancelled.Status);
        Assert.True((await f.Changes.DeleteAsync(cancelled.Id, default)).IsFailure);

        var throwaway = await f.DraftAsync("Throwaway");
        Assert.True((await f.Changes.DeleteAsync(throwaway.Id, default)).IsSuccess);
    }

    [Fact]
    public async Task ARequest_ThatIsInvalid_OrNamesAnotherProjectsContract_IsRefused()
    {
        var f = new Fixture();
        var foreign = (await f.Contracts.CreateAsync(new CreateContractRequest(
            Tenant, OtherProject, "1", "Elsewhere", "Acme", null, Day, Day, Day.AddDays(10), 1m), default)).Value!;
        var own = (await f.Contracts.CreateAsync(new CreateContractRequest(
            Tenant, Project, "1", "Here", "Acme", null, Day, Day, Day.AddDays(10), 1m), default)).Value!;

        Assert.True((await f.Changes.CreateAsync(new CreateChangeRequestRequest(Tenant, Project, " "), default)).IsFailure);
        Assert.True((await f.Changes.CreateAsync(new CreateChangeRequestRequest(Tenant, Project, "x", (ChangeRequestType)9), default)).IsFailure);
        Assert.True((await f.Changes.CreateAsync(new CreateChangeRequestRequest(Tenant, Project, "x", ContractId: foreign.Id), default)).IsFailure);
        Assert.True((await f.Changes.CreateAsync(new CreateChangeRequestRequest(Tenant, Project, "x", ContractId: own.Id), default)).IsSuccess);
        Assert.True((await f.Changes.GetAsync(Guid.NewGuid(), default)).IsFailure);
    }

    [Fact]
    public async Task AProjectWithChangeRequests_IsNotDeletedSilently()
    {
        var f = new Fixture();
        await f.DraftAsync();
        var participant = new ContractsProjectDeletionParticipant(new ContractRepository(f.Db), f.Repository);

        Assert.Equal("1 change request(s)", await participant.GetBlockingReasonAsync(Tenant, Project, default));
        Assert.Null(await participant.GetBlockingReasonAsync(Tenant, OtherProject, default));
    }

    // ---- the upgrade script and its code twin ----

    private const string ScriptFile = "2026-10-19-add-change-requests.sql";

    private static ContractsDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<ContractsDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(db, "project_contracts", ScriptFile, typeof(ContractsChangeRequestsSchemaUpgrade), ["ChangeRequests"]);
    }

    [Fact]
    public void TheUpgradeScript_OnlyCreates()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly(ScriptFile, typeof(ContractsChangeRequestsSchemaUpgrade));
    }
}
