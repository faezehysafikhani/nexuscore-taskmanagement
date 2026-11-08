using Microsoft.EntityFrameworkCore;
using Nexus.Integrations.ProjectWorkflow.Application;
using NexusCore.SharedKernel.Results;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.Workflow;
using Nexus.Workflow.Application;
using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using Nexus.Workflow.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

public sealed class WorkflowPlaceholderTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly Guid Substitute = Guid.NewGuid();

    private sealed class FakeProjects(params Project[] items) : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(items.SingleOrDefault(p => p.Id == id));
        public Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(Project project, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveAsync(Project project, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Fixture
    {
        public WorkflowDbContext Db { get; } = new(new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public WorkflowDefinitionRepository Definitions { get; }
        public WorkflowInstanceRepository Instances { get; }
        public WorkflowDelegationRepository Delegations { get; }
        public WorkflowDefinitionService Service { get; }
        public Project Project { get; } = new(Guid.NewGuid(), Tenant, "P", "P1", ProjectType.Waterfall, Manager, Owner);
        public WorkflowApprovalRequester Requester { get; }

        public Fixture()
        {
            Definitions = new WorkflowDefinitionRepository(Db);
            Instances = new WorkflowInstanceRepository(Db);
            Delegations = new WorkflowDelegationRepository(Db);
            Service = new WorkflowDefinitionService(Definitions, Db);
            Requester = new WorkflowApprovalRequester(Definitions, Instances, Db, [new ProjectApprovalParticipantResolver(new FakeProjects(Project))]);
        }

        public WorkflowInstanceService Center(bool strict) =>
            new(Instances, Db, Definitions, Delegations, TimeProvider.System, new WorkflowOptions { RequireDesignatedApprover = strict });

        public Task Define(string subjectType, params AddWorkflowStepRequest[] steps) =>
            Service.SetStepsAsync(new SetWorkflowStepsRequest(Tenant, subjectType, null, null, null, steps), default);

        public async Task<WorkflowInstance?> Submit(string subjectType, Guid? subjectId = null, IReadOnlyDictionary<string, Guid>? participants = null, bool inProject = true)
        {
            var id = subjectId ?? Guid.NewGuid();
            var outcome = await Requester.RequestApprovalAsync(
                new ApprovalSubject(subjectType, id, Tenant, inProject ? "Project" : null, inProject ? Project.Id : null, participants), default);
            return outcome == ApprovalRequestOutcome.Submitted ? await Db.WorkflowInstances.SingleAsync(i => i.SubjectId == id) : null;
        }
    }

    private static AddWorkflowStepRequest ByKey(string name, string key, bool optional = false) => new(name, null, null, key, optional);

    // -------------------------------------------------------------- placeholders

    [Fact]
    public async Task AStepNamingTheManager_IsResolvedToTheProjectsManager_WhenTheApprovalStarts()
    {
        var f = new Fixture();
        await f.Define("Risk", ByKey("Review", ApprovalParticipants.Manager));

        var instance = await f.Submit("Risk");

        var step = Assert.Single(instance!.GetSteps()!);
        Assert.Equal(Manager, step.ApproverUserId);
        Assert.Equal("Review", step.Name);
        var mine = (await f.Center(false).ListPendingForApproverAsync(Tenant, Manager, default)).Value!;
        Assert.Single(mine);
        Assert.Equal(Manager, mine[0].CurrentApproverUserId);
        Assert.Equal("Review", mine[0].Steps![0].Name);
    }

    [Fact]
    public async Task TheOwnerFallsBackToTheManager_WhenTheProjectHasNoOwner()
    {
        var f = new Fixture();
        var ownerless = new Fixture();
        await ownerless.Define("Risk", ByKey("Final", ApprovalParticipants.Owner));

        // a project with no owner
        var noOwner = new Project(Guid.NewGuid(), Tenant, "Q", "Q1", ProjectType.Waterfall, Manager, null);
        var requester = new WorkflowApprovalRequester(ownerless.Definitions, ownerless.Instances, ownerless.Db, [new ProjectApprovalParticipantResolver(new FakeProjects(noOwner))]);
        var subjectId = Guid.NewGuid();
        await requester.RequestApprovalAsync(new ApprovalSubject("Risk", subjectId, Tenant, "Project", noOwner.Id), default);
        var instance = await ownerless.Db.WorkflowInstances.SingleAsync(i => i.SubjectId == subjectId);

        Assert.Equal(Manager, instance.GetSteps()![0].ApproverUserId);

        await f.Define("Risk", ByKey("Final", ApprovalParticipants.Owner));
        Assert.Equal(Owner, (await f.Submit("Risk"))!.GetSteps()![0].ApproverUserId);
    }

    [Fact]
    public async Task ParticipantsTheSubmitterSupplies_FillWhatTheProjectDoesNotKnow()
    {
        var f = new Fixture();
        await f.Define("Action", ByKey("First", ApprovalParticipants.Approver1), ByKey("Second", ApprovalParticipants.Approver2));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var instance = await f.Submit("Action", participants: new Dictionary<string, Guid>
        {
            [ApprovalParticipants.Approver1] = first, [ApprovalParticipants.Approver2] = second, [ApprovalParticipants.Manager] = Stranger
        });

        Assert.Equal([first, second], instance!.GetSteps()!.Select(s => s.ApproverUserId).ToArray());
    }

    [Fact]
    public async Task AnOptionalStep_IsSkippedWhenItsParticipantIsUnknown_AndRenumbered()
    {
        var f = new Fixture();
        await f.Define("Action", ByKey("One", ApprovalParticipants.Approver1), ByKey("Two", ApprovalParticipants.Approver2, optional: true), ByKey("Three", ApprovalParticipants.Approver3, optional: true));
        var one = Guid.NewGuid();
        var three = Guid.NewGuid();

        var instance = await f.Submit("Action", participants: new Dictionary<string, Guid>
        {
            [ApprovalParticipants.Approver1] = one, [ApprovalParticipants.Approver3] = three
        });

        Assert.Equal(2, instance!.TotalSteps);
        Assert.Equal([(1, "One", (Guid?)one), (2, "Three", three)], instance.GetSteps()!.Select(s => (s.Order, s.Name, s.ApproverUserId)).ToArray());
    }

    [Fact]
    public async Task WhenEveryStepIsOptionalAndUnknown_NoApprovalIsStarted()
    {
        var f = new Fixture();
        await f.Define("Action", ByKey("Two", ApprovalParticipants.Approver2, optional: true));

        Assert.Null(await f.Submit("Action"));
    }

    [Fact]
    public async Task ARequiredPlaceholderThatCannotBeResolved_LeavesTheStepOpenToAnyoneWithPermission()
    {
        var f = new Fixture();
        await f.Define("Risk", ByKey("Review", ApprovalParticipants.Manager));

        var instance = await f.Submit("Risk", inProject: false); // no project, so nobody to resolve to

        Assert.Null(instance!.GetSteps()![0].ApproverUserId);
        Assert.Single((await f.Center(true).ListPendingForApproverAsync(Tenant, Stranger, default)).Value!);
    }

    [Fact]
    public async Task ARunningApproval_KeepsItsSteps_WhenTheDefinitionIsEditedLater()
    {
        var f = new Fixture();
        await f.Define("Risk", ByKey("Review", ApprovalParticipants.Manager));
        var instance = await f.Submit("Risk");

        await f.Define("Risk", new AddWorkflowStepRequest("Other", Stranger, null), new AddWorkflowStepRequest("More", Stranger, null));

        Assert.Equal(1, instance!.TotalSteps);
        Assert.Equal(Manager, (await f.Center(false).GetAsync(instance.Id, default)).Value!.CurrentApproverUserId);
        Assert.Equal(2, (await f.Submit("Risk"))!.TotalSteps); // a new approval gets the new chain
    }

    [Fact]
    public async Task ApprovalsStartedBeforeSnapshots_StillReadTheDefinition()
    {
        var f = new Fixture();
        var definition = new WorkflowDefinition(Guid.NewGuid(), Tenant, "Risk flow", "Risk");
        definition.AddStep(Guid.NewGuid(), "Step", Manager, null);
        await f.Definitions.AddAsync(definition, default);
        var legacy = new WorkflowInstance(Guid.NewGuid(), Tenant, definition.Id, "Risk", Guid.NewGuid(), 1);
        await f.Instances.AddAsync(legacy, default);
        await f.Db.SaveChangesAsync();

        Assert.Null(legacy.GetSteps());
        Assert.Single((await f.Center(false).ListPendingForApproverAsync(Tenant, Manager, default)).Value!);
        Assert.Empty((await f.Center(false).ListPendingForApproverAsync(Tenant, Stranger, default)).Value!);
        Assert.Equal(Manager, (await f.Center(false).GetAsync(legacy.Id, default)).Value!.CurrentApproverUserId);
    }

    // -------------------------------------------------------------- strict approver

    [Fact]
    public async Task WithTheOptionOff_AnyoneWithPermissionCanDecide_AsBefore()
    {
        var f = new Fixture();
        await f.Define("Risk", ByKey("Review", ApprovalParticipants.Manager));
        var instance = await f.Submit("Risk");

        var decided = await f.Center(false).ApproveAsync(instance!.Id, Stranger, new DecideWorkflowInstanceRequest(null), default);

        Assert.True(decided.IsSuccess);
    }

    [Fact]
    public async Task WithTheOptionOn_OnlyTheDesignatedApprover_ASubstitute_OrAnAdministratorCanDecide()
    {
        var f = new Fixture();
        await f.Define("Risk", ByKey("Review", ApprovalParticipants.Manager));
        var center = f.Center(true);
        var request = new DecideWorkflowInstanceRequest(null);

        var first = await f.Submit("Risk");
        var refused = await center.ApproveAsync(first!.Id, Stranger, request, default);
        Assert.Equal("forbidden", refused.Error.Code);
        Assert.Equal(WorkflowInstanceStatus.InProgress, (await center.GetAsync(first.Id, default)).Value!.Status);
        Assert.Equal("forbidden", (await center.RejectAsync(first.Id, Stranger, request, default)).Error.Code);

        Assert.True((await center.ApproveAsync(first.Id, Manager, request, default)).IsSuccess);

        var second = await f.Submit("Risk");
        Assert.True((await center.ApproveAsync(second!.Id, Stranger, request, canActForAnyone: true, default)).IsSuccess);

        var third = await f.Submit("Risk");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await new WorkflowDelegationService(f.Delegations, f.Db).CreateAsync(
            Tenant, Manager, false, new CreateWorkflowDelegationRequest(Substitute, today.AddDays(-1), today.AddDays(2)), default);
        var viaDelegate = (await center.ApproveAsync(third!.Id, Substitute, request, default)).Value!;
        Assert.Equal(Manager, viaDelegate.Decisions.Single().OnBehalfOfUserId);
    }

    // -------------------------------------------------------------- the definition endpoint's service

    [Fact]
    public async Task SetSteps_CreatesTheDefinition_ThenReplacesItsSteps_AndRevivesAResetOverride()
    {
        var f = new Fixture();
        var request = new SetWorkflowStepsRequest(Tenant, "Risk", "Project", f.Project.Id, "Risk (project)", [ByKey("A", ApprovalParticipants.Manager)]);

        var created = (await f.Service.SetStepsAsync(request, default)).Value!;
        var replaced = (await f.Service.SetStepsAsync(request with { Steps = [ByKey("B", ApprovalParticipants.Owner), ByKey("C", ApprovalParticipants.Manager, true)] }, default)).Value!;

        Assert.Equal(created.Id, replaced.Id);
        Assert.Equal(["B", "C"], replaced.Steps.Select(s => s.Name).ToArray());
        Assert.Equal([1, 2], replaced.Steps.Select(s => s.Order).ToArray());
        Assert.True(replaced.Steps[1].IsOptional);

        await f.Service.ResetToDefaultAsync(created.Id, default);
        var revived = (await f.Service.SetStepsAsync(request, default)).Value!;
        Assert.Equal(created.Id, revived.Id);
        Assert.True(revived.IsActive);
        Assert.Single(revived.Steps);

        Assert.Equal("validation.error", (await f.Service.SetStepsAsync(request with { Steps = [new AddWorkflowStepRequest(" ", null, null)] }, default)).Error.Code);
    }

    [Fact]
    public async Task AnEmptyOverride_MeansNoApprovalIsNeededInThatProject()
    {
        var f = new Fixture();
        await f.Define("Risk", ByKey("Review", ApprovalParticipants.Manager));
        await f.Service.SetStepsAsync(new SetWorkflowStepsRequest(Tenant, "Risk", "Project", f.Project.Id, null, []), default);

        Assert.Null(await f.Submit("Risk"));
        Assert.NotNull(await f.Submit("Risk", inProject: false));
    }

    // -------------------------------------------------------------- the project settings (groups)

    private static ProjectWorkflowSettingsService Settings(Fixture f) => new(new FakeProjects(f.Project), f.Service);

    [Fact]
    public async Task Settings_StartUnconfigured_ApplyDefaultsCreatesTheStandardChains_AndLeavesEditedOnesAlone()
    {
        var f = new Fixture();
        var settings = Settings(f);

        var before = (await settings.GetAsync(Tenant, null, default)).Value!;
        Assert.All(before, g => Assert.False(g.IsConfigured));
        Assert.Equal(["تایید اولیه", "تایید نهایی"], before.Single(g => g.Key == "documents").DefaultSteps.Select(s => s.Name).ToArray());

        await settings.SaveGeneralAsync(Tenant, "risks", new SaveWorkflowGroupRequest([new WorkflowStepSettingDto("Mine", Stranger, null)]), default);
        var after = (await settings.ApplyDefaultsAsync(Tenant, default)).Value!;

        Assert.All(after, g => Assert.True(g.IsConfigured));
        Assert.Equal("Mine", after.Single(g => g.Key == "risks").Steps.Single().Name);
        var documents = after.Single(g => g.Key == "documents");
        Assert.Equal([ApprovalParticipants.Manager, ApprovalParticipants.Owner], documents.Steps.Select(s => s.ApproverKey!).ToArray());
        // a group applies to every subject type it covers
        var risks = (await f.Service.ListAsync(Tenant, null, default)).Value!.Where(d => d.ScopeType == "General" && d.IsActive).Select(d => d.SubjectType).ToList();
        Assert.Contains("Risk", risks);
        Assert.Contains("RiskResponseStatus", risks);
        var actions = after.Single(g => g.Key == "actions");
        Assert.Equal([false, true, true], actions.Steps.Select(s => s.IsOptional).ToArray());
    }

    [Fact]
    public async Task ASavedProjectChain_ShadowsTheGeneralOne_UntilItIsReset()
    {
        var f = new Fixture();
        var settings = Settings(f);
        await settings.ApplyDefaultsAsync(Tenant, default);

        var saved = (await settings.SaveProjectAsync(Tenant, f.Project.Id, "risks",
            new SaveWorkflowGroupRequest([new WorkflowStepSettingDto("Special", Stranger, null), new WorkflowStepSettingDto("Boss", null, ApprovalParticipants.Owner)]), Manager, false, default)).Value!;

        var risks = saved.Single(g => g.Key == "risks");
        Assert.True(risks.IsCustom);
        Assert.Equal(["Special", "Boss"], risks.Steps.Select(s => s.Name).ToArray());
        Assert.False((await settings.GetAsync(Tenant, null, default)).Value!.Single(g => g.Key == "risks").IsCustom);
        Assert.False(saved.Single(g => g.Key == "stakeholders").IsCustom);

        var instance = await f.Submit("RiskResponseStatus"); // the project chain covers the group's other subject type too
        Assert.Equal([Stranger, Owner], instance!.GetSteps()!.Select(s => s.ApproverUserId!.Value).ToArray());

        var reset = (await settings.ResetProjectAsync(Tenant, f.Project.Id, "risks", Owner, false, default)).Value!.Single(g => g.Key == "risks");
        Assert.False(reset.IsCustom);
        Assert.Equal("تایید نهایی", reset.Steps.Single().Name);
    }

    [Fact]
    public async Task Settings_RefuseWhatCannotWork()
    {
        var f = new Fixture();
        var settings = Settings(f);
        Task<string> Code(string group, params WorkflowStepSettingDto[] steps) =>
            settings.SaveGeneralAsync(Tenant, group, new SaveWorkflowGroupRequest(steps), default).ContinueWith(t => t.Result.Error.Code);

        Assert.Equal("not_found", await Code("nonsense"));
        Assert.Equal("validation.error", await Code("risks", new WorkflowStepSettingDto("A", null, null)));
        Assert.Equal("validation.error", await Code("risks", new WorkflowStepSettingDto("A", Stranger, "Manager")));
        Assert.Equal("validation.error", await Code("risks", new WorkflowStepSettingDto("A", null, "Bogus")));
        Assert.Equal("validation.error", await Code("risks", new WorkflowStepSettingDto(" ", null, "Manager")));
        // only the project's manager or owner (or an administrator) may change its rules
        Assert.Equal("forbidden", (await settings.SaveProjectAsync(Tenant, f.Project.Id, "risks", new SaveWorkflowGroupRequest(null), Stranger, false, default)).Error.Code);
        Assert.Equal("forbidden", (await settings.ResetProjectAsync(Tenant, f.Project.Id, "risks", Stranger, false, default)).Error.Code);
        Assert.True((await settings.SaveProjectAsync(Tenant, f.Project.Id, "risks", new SaveWorkflowGroupRequest(null), Stranger, true, default)).IsSuccess);
        Assert.Equal("not_found", (await settings.SaveProjectAsync(Tenant, Guid.NewGuid(), "risks", new SaveWorkflowGroupRequest(null), Manager, true, default)).Error.Code);
        Assert.Equal("not_found", (await settings.ResetProjectAsync(Tenant, f.Project.Id, "nonsense", Manager, false, default)).Error.Code);
    }

    // -------------------------------------------------------------- schema

    private static WorkflowDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "workflow", "2026-10-15-add-workflow-approver-placeholders.sql", typeof(WorkflowApproverPlaceholdersSchemaUpgrade),
            [],
            ("WorkflowSteps", "[ApproverKey] nvarchar(40) NULL"),
            ("WorkflowInstances", "[StepsSnapshot] nvarchar(max) NULL"));
    }

    [Fact]
    public void IsOptional_IsAddedWithADefault_ThatTheModelItselfDoesNotCarry()
    {
        using var db = NewSqlServerContext();
        Assert.Contains("[IsOptional] bit NOT NULL", SchemaUpgradeVerifier.ColumnLines(db.Database.GenerateCreateScript(), "workflow", "WorkflowSteps"));

        foreach (var text in new[] { SchemaUpgradeVerifier.Normalise(SchemaUpgradeVerifier.ReadScript("2026-10-15-add-workflow-approver-placeholders.sql")),
                     SchemaUpgradeVerifier.Normalise(string.Join("\n", SchemaUpgradeVerifier.ReadHelperStatements(typeof(WorkflowApproverPlaceholdersSchemaUpgrade)))) })
        {
            Assert.Contains("COL_LENGTH(N'workflow.WorkflowSteps', N'IsOptional') IS NULL", text);
            Assert.Contains("ADD [IsOptional] bit NOT NULL CONSTRAINT [DF_WorkflowSteps_IsOptional] DEFAULT CAST(0 AS bit)", text);
        }
    }

    [Fact]
    public void TheUpgrade_OnlyAddsThings()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-15-add-workflow-approver-placeholders.sql", typeof(WorkflowApproverPlaceholdersSchemaUpgrade));
    }
}
