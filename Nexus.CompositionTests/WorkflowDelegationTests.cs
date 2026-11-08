using Microsoft.EntityFrameworkCore;
using Nexus.Workflow.Application;
using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using Nexus.Workflow.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

public sealed class WorkflowDelegationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid(); // the designated approver
    private static readonly Guid Bob = Guid.NewGuid();   // her substitute
    private static readonly Guid Carol = Guid.NewGuid(); // someone else
    private static readonly DateOnly Today = new(2026, 6, 10);

    private sealed class FixedTime(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    private sealed class Fixture
    {
        public WorkflowDbContext Db { get; }
        public WorkflowDefinitionRepository Definitions { get; }
        public WorkflowInstanceRepository Instances { get; }
        public WorkflowDelegationRepository Delegations { get; }
        public WorkflowInstanceService Center { get; }
        public WorkflowDelegationService Delegation { get; }
        private readonly WorkflowApprovalRequester _requester;

        public Fixture(DateOnly? today = null)
        {
            var options = new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            Db = new WorkflowDbContext(options);
            Definitions = new WorkflowDefinitionRepository(Db);
            Instances = new WorkflowInstanceRepository(Db);
            Delegations = new WorkflowDelegationRepository(Db);
            var time = new FixedTime(today ?? Today);
            Center = new WorkflowInstanceService(Instances, Db, Definitions, Delegations, time);
            Delegation = new WorkflowDelegationService(Delegations, Db, time);
            _requester = new WorkflowApprovalRequester(Definitions, Instances, Db);
        }

        /// <summary>A workflow for the subject type whose steps are designated to the given approvers, in order.</summary>
        public async Task DefineAsync(string subjectType, params Guid?[] approvers)
        {
            var definition = new WorkflowDefinition(Guid.NewGuid(), Tenant, subjectType + " flow", subjectType);
            foreach (var approver in approvers)
            {
                definition.AddStep(Guid.NewGuid(), "Step", approver, null);
            }

            await Definitions.AddAsync(definition, default);
            await Db.SaveChangesAsync();
        }

        public async Task<Guid> SubmitAsync(string subjectType)
        {
            var subjectId = Guid.NewGuid();
            Assert.Equal(ApprovalRequestOutcome.Submitted, await _requester.RequestApprovalAsync(new ApprovalSubject(subjectType, subjectId, Tenant), default));
            return (await Db.WorkflowInstances.SingleAsync(i => i.SubjectId == subjectId)).Id;
        }

        public async Task<WorkflowDelegationDto> DelegateAsync(
            Guid from, Guid to, int startOffset = -1, int endOffset = 5, string? subjectType = null, Guid? current = null, bool admin = false, DateOnly? today = null)
        {
            var day = today ?? Today;
            var result = await Delegation.CreateAsync(
                Tenant, current ?? from, admin, new CreateWorkflowDelegationRequest(to, day.AddDays(startOffset), day.AddDays(endOffset), subjectType, "holiday", from), default);
            Assert.True(result.IsSuccess, result.Error.Message);
            return result.Value!;
        }

        public async Task<IReadOnlyList<WorkflowInstanceDto>> CenterOfAsync(Guid user) =>
            (await Center.ListPendingForApproverAsync(Tenant, user, default)).Value!;
    }

    // ------------------------------------------------------------ approval center

    [Fact]
    public async Task WithoutADelegation_TheSubstituteSeesNothingOfTheApproversWork()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        await f.SubmitAsync("Risk");

        Assert.Single(await f.CenterOfAsync(Alice));
        Assert.Empty(await f.CenterOfAsync(Bob));
    }

    [Fact]
    public async Task ADelegation_ShowsTheApproversPendingItemsToTheSubstitute_AndSaysWhoseTheyAre()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        await f.DefineAsync("Stakeholder", Carol);
        var risk = await f.SubmitAsync("Risk");
        await f.SubmitAsync("Stakeholder");
        await f.DelegateAsync(Alice, Bob);

        var bobs = await f.CenterOfAsync(Bob);

        var item = Assert.Single(bobs); // Carol's item stays hers
        Assert.Equal(risk, item.Id);
        Assert.Equal(Alice, item.CurrentApproverUserId);
        Assert.Equal(Alice, item.DelegatedFromUserId);

        var alices = Assert.Single(await f.CenterOfAsync(Alice));
        Assert.Null(alices.DelegatedFromUserId); // hers in her own right; she still sees it
    }

    [Fact]
    public async Task ADelegationForOneSubjectType_OnlyCoversThatType()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        await f.DefineAsync("Stakeholder", Alice);
        await f.SubmitAsync("Risk");
        await f.SubmitAsync("Stakeholder");
        await f.DelegateAsync(Alice, Bob, subjectType: "Risk");

        Assert.Equal("Risk", Assert.Single(await f.CenterOfAsync(Bob)).SubjectType);
    }

    [Theory]
    [InlineData(1, 5)]    // starts tomorrow
    [InlineData(-9, -1)]  // ended yesterday
    public async Task ADelegationOutsideItsDates_GivesNothing(int startOffset, int endOffset)
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        await f.SubmitAsync("Risk");
        // Stored directly: the service refuses to create a delegation that has already ended.
        f.Db.WorkflowDelegations.Add(new WorkflowDelegation(Guid.NewGuid(), Tenant, Alice, Bob, Today.AddDays(startOffset), Today.AddDays(endOffset), null, null));
        await f.Db.SaveChangesAsync();

        Assert.Empty(await f.CenterOfAsync(Bob));
        Assert.Single(await f.CenterOfAsync(Alice));
    }

    [Fact]
    public async Task ARevokedDelegation_StopsAtOnce()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        await f.SubmitAsync("Risk");
        var delegation = await f.DelegateAsync(Alice, Bob);
        Assert.Single(await f.CenterOfAsync(Bob));

        var revoked = await f.Delegation.RevokeAsync(Tenant, delegation.Id, Alice, false, default);

        Assert.True(revoked.Value!.IsRevoked);
        Assert.False(revoked.Value.IsActiveNow);
        Assert.Empty(await f.CenterOfAsync(Bob));
    }

    [Fact]
    public async Task TheDelegationAppliesToTheCurrentStepOnly()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Carol, Alice); // step 1 is Carol's, step 2 Alice's
        var instance = await f.SubmitAsync("Risk");
        await f.DelegateAsync(Alice, Bob);

        Assert.Empty(await f.CenterOfAsync(Bob)); // still waiting on Carol

        await f.Center.ApproveAsync(instance, Carol, new DecideWorkflowInstanceRequest(null), default);

        Assert.Single(await f.CenterOfAsync(Bob));
    }

    [Fact]
    public async Task DelegationIsNotTransitive()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        await f.SubmitAsync("Risk");
        await f.DelegateAsync(Alice, Bob);
        await f.DelegateAsync(Bob, Carol);

        Assert.Empty(await f.CenterOfAsync(Carol));
    }

    [Fact]
    public async Task AnotherTenantsDelegation_IsIgnored()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        await f.SubmitAsync("Risk");
        f.Db.WorkflowDelegations.Add(new WorkflowDelegation(Guid.NewGuid(), Guid.NewGuid(), Alice, Bob, Today.AddDays(-1), Today.AddDays(5), null, null));
        await f.Db.SaveChangesAsync();

        Assert.Empty(await f.CenterOfAsync(Bob));
    }

    // ------------------------------------------------------------------ deciding

    [Fact]
    public async Task ASubstituteDecision_RecordsOnWhoseBehalf_AndConcludesTheApproval()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        var instance = await f.SubmitAsync("Risk");
        await f.DelegateAsync(Alice, Bob);

        var decided = (await f.Center.ApproveAsync(instance, Bob, new DecideWorkflowInstanceRequest("fine"), default)).Value!;

        Assert.Equal(WorkflowInstanceStatus.Approved, decided.Status);
        var decision = Assert.Single(decided.Decisions);
        Assert.Equal(Bob, decision.DecidedByUserId);
        Assert.Equal(Alice, decision.OnBehalfOfUserId);

        // It is stored, not just returned.
        var reloaded = (await f.Center.GetAsync(instance, default)).Value!;
        Assert.Equal(Alice, reloaded.Decisions.Single().OnBehalfOfUserId);
    }

    [Fact]
    public async Task ASubstituteRejection_IsRecordedTheSameWay()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        var instance = await f.SubmitAsync("Risk");
        await f.DelegateAsync(Alice, Bob);

        var decided = (await f.Center.RejectAsync(instance, Bob, new DecideWorkflowInstanceRequest("no"), default)).Value!;

        Assert.Equal(WorkflowInstanceStatus.Rejected, decided.Status);
        Assert.Equal(Alice, decided.Decisions.Single().OnBehalfOfUserId);
    }

    [Fact]
    public async Task TheApproversOwnDecision_AndAnyoneElses_AreNotMarkedAsOnBehalf()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        var own = await f.SubmitAsync("Risk");
        var other = await f.SubmitAsync("Risk");
        await f.DelegateAsync(Alice, Bob);

        var byAlice = (await f.Center.ApproveAsync(own, Alice, new DecideWorkflowInstanceRequest(null), default)).Value!;
        // Carol holds no delegation: still allowed, as before delegation existed, but not recorded as on Alice's behalf.
        var byCarol = (await f.Center.ApproveAsync(other, Carol, new DecideWorkflowInstanceRequest(null), default)).Value!;

        Assert.Null(byAlice.Decisions.Single().OnBehalfOfUserId);
        Assert.Null(byCarol.Decisions.Single().OnBehalfOfUserId);
    }

    [Fact]
    public async Task ADelegationForAnotherSubjectType_DoesNotMarkTheDecision()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        var instance = await f.SubmitAsync("Risk");
        await f.DelegateAsync(Alice, Bob, subjectType: "Stakeholder");

        var decided = (await f.Center.ApproveAsync(instance, Bob, new DecideWorkflowInstanceRequest(null), default)).Value!;

        Assert.Null(decided.Decisions.Single().OnBehalfOfUserId);
    }

    [Fact]
    public async Task WithoutDelegationSupportWired_TheCenterBehavesAsBefore()
    {
        var f = new Fixture();
        await f.DefineAsync("Risk", Alice);
        var instance = await f.SubmitAsync("Risk");
        var legacy = new WorkflowInstanceService(f.Instances, f.Db);

        Assert.Single((await legacy.ListPendingForApproverAsync(Tenant, Alice, default)).Value!);
        Assert.Empty((await legacy.ListPendingForApproverAsync(Tenant, Bob, default)).Value!);
        var decided = (await legacy.ApproveAsync(instance, Bob, new DecideWorkflowInstanceRequest(null), default)).Value!;
        Assert.Null(decided.Decisions.Single().OnBehalfOfUserId);
        Assert.Null(decided.CurrentApproverUserId);
    }

    // ---------------------------------------------------------- creating / revoking

    [Fact]
    public async Task Create_Validates()
    {
        var f = new Fixture();
        CreateWorkflowDelegationRequest Req(Guid to, int start, int end, Guid? from = null) =>
            new(to, Today.AddDays(start), Today.AddDays(end), null, null, from);

        Assert.Equal("validation.error", (await f.Delegation.CreateAsync(Tenant, Alice, false, Req(Alice, 0, 3), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Delegation.CreateAsync(Tenant, Alice, false, Req(Guid.Empty, 0, 3), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Delegation.CreateAsync(Tenant, Alice, false, Req(Bob, 3, 0), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Delegation.CreateAsync(Tenant, Alice, false, Req(Bob, -5, -1), default)).Error.Code); // already over
        Assert.True((await f.Delegation.CreateAsync(Tenant, Alice, false, Req(Bob, 0, 0), default)).IsSuccess); // a single day is fine
    }

    [Fact]
    public async Task OnlyAnAdministrator_CanDelegateSomeoneElsesApprovals()
    {
        var f = new Fixture();
        var request = new CreateWorkflowDelegationRequest(Bob, Today, Today.AddDays(3), null, null, Alice);

        Assert.Equal("forbidden", (await f.Delegation.CreateAsync(Tenant, Carol, false, request, default)).Error.Code);

        var byAdmin = await f.Delegation.CreateAsync(Tenant, Carol, true, request, default);
        Assert.Equal(Alice, byAdmin.Value!.DelegatorUserId);
    }

    [Fact]
    public async Task Create_RefusesAnOverlappingDelegationBetweenTheSamePair_ButNotOtherCombinations()
    {
        var f = new Fixture();
        await f.DelegateAsync(Alice, Bob, 0, 5);

        var overlap = await f.Delegation.CreateAsync(Tenant, Alice, false, new CreateWorkflowDelegationRequest(Bob, Today.AddDays(3), Today.AddDays(8)), default);
        Assert.Equal("conflict", overlap.Error.Code);

        Assert.True((await f.Delegation.CreateAsync(Tenant, Alice, false, new CreateWorkflowDelegationRequest(Bob, Today.AddDays(6), Today.AddDays(8)), default)).IsSuccess); // after
        Assert.True((await f.Delegation.CreateAsync(Tenant, Alice, false, new CreateWorkflowDelegationRequest(Carol, Today, Today.AddDays(5)), default)).IsSuccess); // another substitute
    }

    [Fact]
    public async Task OverlapWithADifferentSubjectType_IsAllowed_ButAnAllTypesOneOverlapsEverything()
    {
        var f = new Fixture();
        await f.DelegateAsync(Alice, Bob, 0, 5, subjectType: "Risk");

        Assert.True((await f.Delegation.CreateAsync(Tenant, Alice, false, new CreateWorkflowDelegationRequest(Bob, Today, Today.AddDays(5), "Stakeholder"), default)).IsSuccess);
        Assert.Equal("conflict", (await f.Delegation.CreateAsync(Tenant, Alice, false, new CreateWorkflowDelegationRequest(Bob, Today, Today.AddDays(5)), default)).Error.Code);
    }

    [Fact]
    public async Task ARevokedDelegation_NoLongerBlocksANewOne()
    {
        var f = new Fixture();
        var first = await f.DelegateAsync(Alice, Bob, 0, 5);
        await f.Delegation.RevokeAsync(Tenant, first.Id, Alice, false, default);

        Assert.True((await f.Delegation.CreateAsync(Tenant, Alice, false, new CreateWorkflowDelegationRequest(Bob, Today, Today.AddDays(5)), default)).IsSuccess);
    }

    [Fact]
    public async Task Revoke_IsForTheDelegatorOrAnAdministrator_InTheirOwnTenant_AndRepeatable()
    {
        var f = new Fixture();
        var delegation = await f.DelegateAsync(Alice, Bob);

        Assert.Equal("forbidden", (await f.Delegation.RevokeAsync(Tenant, delegation.Id, Bob, false, default)).Error.Code);
        Assert.Equal("not_found", (await f.Delegation.RevokeAsync(Guid.NewGuid(), delegation.Id, Alice, false, default)).Error.Code);
        Assert.Equal("not_found", (await f.Delegation.RevokeAsync(Tenant, Guid.NewGuid(), Alice, false, default)).Error.Code);

        Assert.True((await f.Delegation.RevokeAsync(Tenant, delegation.Id, Carol, true, default)).Value!.IsRevoked);
        Assert.True((await f.Delegation.RevokeAsync(Tenant, delegation.Id, Alice, false, default)).IsSuccess); // again: no error
    }

    [Fact]
    public async Task TheList_ShowsDelegationsGivenAndReceived_NewestFirst_WithWhetherTheyAreInForce()
    {
        var f = new Fixture();
        await f.DelegateAsync(Alice, Bob, -1, 5);          // in force
        await f.DelegateAsync(Alice, Carol, 10, 12);        // starts later
        await f.DelegateAsync(Carol, Alice, -1, 2);         // received by Alice

        var mine = (await f.Delegation.ListMineAsync(Tenant, Alice, default)).Value!;

        Assert.Equal(3, mine.Count);
        Assert.Equal(Today.AddDays(10), mine[0].StartDate);
        Assert.Equal([false, true, true], mine.Select(d => d.IsActiveNow));
        Assert.Single((await f.Delegation.ListMineAsync(Tenant, Bob, default)).Value!);
        Assert.Empty((await f.Delegation.ListMineAsync(Guid.NewGuid(), Alice, default)).Value!);
    }

    // --------------------------------------------------------------------- schema

    private static WorkflowDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "workflow", "2026-10-04-add-workflow-delegation.sql", typeof(WorkflowSchemaUpgrade),
            ["WorkflowDelegations"],
            ("WorkflowDecisions", "[OnBehalfOfUserId] uniqueidentifier NULL"));
    }

    [Fact]
    public void TheUpgrade_OnlyAddsThings()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-04-add-workflow-delegation.sql", typeof(WorkflowSchemaUpgrade));
    }
}
