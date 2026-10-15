using Microsoft.EntityFrameworkCore;
using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.Integrations.ProjectGovernance.Application;
using Nexus.Integrations.ProjectVisibility.Application;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using Nexus.Portfolio.Application;
using Nexus.Portfolio.Application.Dtos;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Delegation.Application;
using Nexus.ProjectManagement.Delegation.Application.Dtos;
using Nexus.ProjectManagement.Delegation.Domain;
using Nexus.ProjectManagement.Delegation.Infrastructure;
using Nexus.ProjectManagement.Team.Application;
using Nexus.ProjectManagement.Team.Domain;
using Nexus.ProjectManagement.Waterfall.Application;
using NexusCore.Application.Platform.Dtos;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Auditing;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

public sealed class ProjectGovernanceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();
    private static readonly Guid Dave = Guid.NewGuid();

    // ----------------------------------------------------------------- fakes

    private sealed class FakePlatform : IPlatformRepository
    {
        public List<SystemSetting> Settings { get; } = [];
        public Task AddAuditLogAsync(AuditLog auditLog, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(AuditLogQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SystemSetting>> ListSettingsAsync(Guid? tenantId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SystemSetting>>(Settings.Where(s => s.TenantId == tenantId).ToList());
        public Task<SystemSetting?> FindSettingAsync(Guid? tenantId, string key, string scope, CancellationToken ct) =>
            Task.FromResult(Settings.SingleOrDefault(s => s.TenantId == tenantId && s.Key == key && s.Scope == scope));
        public Task AddSettingAsync(SystemSetting setting, CancellationToken ct) { Settings.Add(setting); return Task.CompletedTask; }
    }

    private sealed class Uow : IUnitOfWork
    {
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { Saves++; return Task.FromResult(1); }
    }

    private sealed class FakeProjects(params Project[] projects) : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(projects.SingleOrDefault(p => p.Id == id));
        public Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken ct) =>
            Task.FromResult(new PagedResult<Project>(projects, request.PageNumber, request.PageSize, projects.Length));
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(Project project, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveAsync(Project project, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeActions(params ActionItem[] actions) : IActionItemRepository
    {
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(actions.SingleOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ActionItem>>(actions);
        public Task AddAsync(ActionItem action, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeTeam(params ProjectMember[] members) : ITeamRepository
    {
        public Task<ProjectMember?> GetMemberByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsMemberAsync(Guid projectId, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListProjectIdsForUserAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(members.Where(m => m.UserId == userId).Select(m => m.ProjectId).ToList());
        public Task AddMemberAsync(ProjectMember member, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveMemberAsync(ProjectMember member, CancellationToken ct) => throw new NotSupportedException();
        public Task<GovernanceRole?> GetGovernanceRoleByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GovernanceRole>> ListGovernanceRolesAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddGovernanceRoleAsync(GovernanceRole role, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveGovernanceRoleAsync(GovernanceRole role, CancellationToken ct) => throw new NotSupportedException();
    }

    private static Project NewProject(string code, Guid? owner = null, Guid? manager = null)
    {
        var project = new Project(Guid.NewGuid(), Tenant, code, code, ProjectType.Waterfall, manager, owner);
        project.UpdateDetails(code, code, manager, owner, null, null, null, null, null, null, null, null, null, null, null);
        return project;
    }

    private static ActionItem NewAction(string title, Guid? owner = null, Guid? responsible = null, IReadOnlyList<Guid>? approvers = null)
    {
        var unit = Guid.NewGuid();
        var action = new ActionItem(Guid.NewGuid(), Tenant, title, unit, Guid.NewGuid(), null);
        action.UpdateDetails(title, null, owner, responsible, unit, Guid.NewGuid(), null, null, null);
        if (approvers is not null)
        {
            action.SetApprovers(approvers);
        }

        return action;
    }

    private static GovernanceSettingsService Settings(FakePlatform platform, Uow? uow = null, params Project[] projects) =>
        new(platform, uow ?? new Uow(), new FakeProjects(projects));

    // -------------------------------------------------------------- settings

    [Fact]
    public async Task WithNothingSaved_EverySwitchIsAtItsOriginalBehaviour()
    {
        var service = Settings(new FakePlatform());

        var settings = (await service.GetAsync(Tenant, default)).Value!;

        Assert.Equal(VisibilityRules.AllOn, settings.Visibility);
        Assert.False(settings.AllowConfirmedProgressOnRejection);
        Assert.Equal(VisibilityRules.AllOn, await ((IVisibilityRules)service).GetAsync(Tenant, default));
        Assert.False(await ((IProgressRejectionPolicy)service).AllowsConfirmedProgressAsync(Tenant, default));
    }

    [Fact]
    public async Task SavedSwitches_AreKeptPerOrganisation_AndAnsweredByTheExtensionPoints()
    {
        var platform = new FakePlatform();
        var uow = new Uow();
        var service = Settings(platform, uow);

        await service.SaveAsync(Tenant, new GovernanceSettingsDto(new VisibilityRules(ViewByOwner: false, ViewByResources: false), true), default);
        await service.SaveAsync(Tenant, new GovernanceSettingsDto(new VisibilityRules(ViewByOwner: false, ViewByResources: false, ViewByOrgHeads: false), true), default);

        Assert.Single(platform.Settings); // updated in place, not duplicated
        Assert.Equal(2, uow.Saves);
        var rules = await ((IVisibilityRules)service).GetAsync(Tenant, default);
        Assert.False(rules.ViewByOwner);
        Assert.False(rules.ViewByOrgHeads);
        Assert.True(rules.ViewByManagerOrResponsible);
        Assert.True(await ((IProgressRejectionPolicy)service).AllowsConfirmedProgressAsync(Tenant, default));
        Assert.Equal(VisibilityRules.AllOn, await ((IVisibilityRules)service).GetAsync(Guid.NewGuid(), default)); // another organisation is unaffected
    }

    [Fact]
    public async Task AnUnreadableRow_CountsAsNeverSaved()
    {
        var platform = new FakePlatform();
        platform.Settings.Add(new SystemSetting(Guid.NewGuid(), Tenant, "organisation", "{not json", GovernanceSettingsService.Scope));

        Assert.Equal(VisibilityRules.AllOn, (await Settings(platform).GetAsync(Tenant, default)).Value!.Visibility);
    }

    [Fact]
    public async Task ProjectSettings_StartWithEverySectionAndEveryoneAllowed_ThenKeepWhatWasSaved()
    {
        var project = NewProject("P1");
        var platform = new FakePlatform();
        var service = Settings(platform, null, project);

        var before = (await service.GetProjectAsync(Tenant, project.Id, default)).Value!;
        Assert.Null(before.EnabledTabs);
        Assert.True(before.ContractAccess.ViewByTeam);

        var saved = await service.SaveProjectAsync(Tenant, project.Id,
            new SaveProjectGovernanceSettingsRequest(["charter", "risks", "risks"], new AccessMatrixDto(ViewByTeam: false), null), default);

        Assert.Equal(["charter", "risks"], saved.Value!.EnabledTabs);
        var again = (await service.GetProjectAsync(Tenant, project.Id, default)).Value!;
        Assert.Equal(["charter", "risks"], again.EnabledTabs);
        Assert.False(again.ContractAccess.ViewByTeam);
        Assert.True(again.InvoiceAccess.ViewByTeam);
        var all = (await service.ListProjectSettingsAsync(Tenant, default)).Value!;
        Assert.Equal(project.Id, Assert.Single(all).ProjectId);
        Assert.Empty((await service.ListProjectSettingsAsync(Guid.NewGuid(), default)).Value!);
    }

    [Fact]
    public async Task ProjectSettings_RefuseAnUnknownSection_AndAProjectThatIsNotThere()
    {
        var project = NewProject("P1");
        var service = Settings(new FakePlatform(), null, project);

        Assert.Equal("validation.error", (await service.SaveProjectAsync(Tenant, project.Id, new SaveProjectGovernanceSettingsRequest(["charter", "nonsense"], null, null), default)).Error.Code);
        Assert.Equal("not_found", (await service.SaveProjectAsync(Tenant, Guid.NewGuid(), new SaveProjectGovernanceSettingsRequest(null, null, null), default)).Error.Code);
        Assert.Equal("not_found", (await Settings(new FakePlatform(), null, NewProject("P2")).SaveProjectAsync(Guid.NewGuid(), project.Id, new SaveProjectGovernanceSettingsRequest(null, null, null), default)).Error.Code);
    }

    // -------------------------------------------------------------- portfolio honours the switches

    private static PortfolioService Portfolio(IVisibilityRules? rules, Project[] projects, ActionItem[] actions, params IVisibilityProvider[] providers) =>
        new(new FakeProjects(projects), new FakeActions(actions), providers, rules);

    private static PortfolioQuery QueryFor(Guid user) => new(Tenant, user, false, null, null, null, null, null, null, null);

    private sealed class FixedRules(VisibilityRules rules) : IVisibilityRules
    {
        public Task<VisibilityRules> GetAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(rules);
    }

    [Fact]
    public async Task WithNoRulesInstalled_OwnersAndManagersSeeTheirWork_AndActionApproversDoNotYet()
    {
        var owned = NewProject("OWN", owner: Alice);
        var managed = NewProject("MAN", manager: Alice);
        var other = NewProject("OTHER", owner: Bob);
        var approvedByAlice = NewAction("approver", approvers: [Alice]);
        var portfolio = Portfolio(null, [owned, managed, other], [approvedByAlice]);

        var result = (await portfolio.GetPortfolioAsync(QueryFor(Alice), default)).Value!;

        Assert.Equal(new[] { "OWN", "MAN" }.Order(), result.Projects.Select(p => p.Code).Order());
        Assert.Empty(result.Actions);
    }

    [Fact]
    public async Task SwitchingOffTheOwner_HidesProjectsAndActionsFromThemUnlessAnotherWayAllowsIt()
    {
        var owned = NewProject("OWN", owner: Alice, manager: Bob);
        var action = NewAction("mine", owner: Alice, responsible: Bob);
        var rules = new FixedRules(new VisibilityRules(ViewByOwner: false));
        var portfolio = Portfolio(rules, [owned], [action]);

        var asOwner = (await portfolio.GetPortfolioAsync(QueryFor(Alice), default)).Value!;
        var asManager = (await portfolio.GetPortfolioAsync(QueryFor(Bob), default)).Value!;

        Assert.Empty(asOwner.Projects);
        Assert.Empty(asOwner.Actions);
        Assert.Single(asManager.Projects);
        Assert.Single(asManager.Actions);
        Assert.Empty((await portfolio.GetVisibleProjectsAsync(QueryFor(Alice), false, default)).Value!);
        Assert.Single((await portfolio.GetVisibleProjectsAsync(QueryFor(Bob), false, default)).Value!);
    }

    [Fact]
    public async Task SwitchingOffTheManager_HidesWhatTheyOnlyManage_AndViewAllStillSeesEverything()
    {
        var managed = NewProject("MAN", owner: Bob, manager: Alice);
        var responsible = NewAction("resp", owner: Bob, responsible: Alice);
        var portfolio = Portfolio(new FixedRules(new VisibilityRules(ViewByManagerOrResponsible: false)), [managed], [responsible]);

        var asManager = (await portfolio.GetPortfolioAsync(QueryFor(Alice), default)).Value!;
        var everything = (await portfolio.GetPortfolioAsync(QueryFor(Carol) with { ViewAll = true }, default)).Value!;

        Assert.Empty(asManager.Projects);
        Assert.Empty(asManager.Actions);
        Assert.Single(everything.Projects);
        Assert.Single(everything.Actions);
    }

    [Fact]
    public async Task AnActionsApprovers_SeeIt_WhenTheApproverSwitchIsOn_AndNotWhenItIsOff()
    {
        var action = NewAction("needs approval", owner: Bob, responsible: Bob, approvers: [Alice, Carol]);

        var on = Portfolio(new FixedRules(VisibilityRules.AllOn), [], [action]);
        var off = Portfolio(new FixedRules(new VisibilityRules(ViewByApprovers: false)), [], [action]);

        Assert.Single((await on.GetPortfolioAsync(QueryFor(Alice), default)).Value!.Actions);
        Assert.Single((await on.GetPortfolioAsync(QueryFor(Carol), default)).Value!.Actions);
        Assert.Empty((await on.GetPortfolioAsync(QueryFor(Dave), default)).Value!.Actions);
        Assert.Empty((await off.GetPortfolioAsync(QueryFor(Alice), default)).Value!.Actions);
    }

    [Fact]
    public async Task TheProviders_GrantNothing_WhenTheirSwitchIsOff()
    {
        var project = Guid.NewGuid();
        var unit = new OrganizationUnit(Guid.NewGuid(), Tenant, "U", "U", null);
        unit.Update("U", "U", null, Alice, true);
        var off = new FixedRules(new VisibilityRules(ViewByResources: false, ViewByOrgHeads: false));
        var on = new FixedRules(VisibilityRules.AllOn);
        var team = new FakeTeam(new ProjectMember(Guid.NewGuid(), Tenant, project, Alice));
        var units = new FakeUnitsRepo(unit);

        Assert.Empty((await new TeamMembershipVisibilityProvider(team, off).GetGrantAsync(Tenant, Alice, default)).ProjectIds);
        Assert.Single((await new TeamMembershipVisibilityProvider(team, on).GetGrantAsync(Tenant, Alice, default)).ProjectIds);
        Assert.Single((await new TeamMembershipVisibilityProvider(team).GetGrantAsync(Tenant, Alice, default)).ProjectIds);
        Assert.Empty((await new OrganizationUnitManagerVisibilityProvider(units, off).GetGrantAsync(Tenant, Alice, default)).OrganizationUnitIds);
        Assert.Single((await new OrganizationUnitManagerVisibilityProvider(units, on).GetGrantAsync(Tenant, Alice, default)).OrganizationUnitIds);
        Assert.Empty((await new PendingApprovalVisibilityProvider(null, off).GetGrantAsync(Tenant, Alice, default)).ProjectIds);
    }

    private sealed class FakeUnitsRepo(params OrganizationUnit[] units) : IOrganizationUnitRepository
    {
        public Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(units.SingleOrDefault(u => u.Id == id));
        public Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrganizationUnit>>(units);
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(OrganizationUnit unit, CancellationToken ct) => Task.CompletedTask;
    }

    // -------------------------------------------------------------- item delegation

    private sealed class DelegationFixture
    {
        public ItemDelegationDbContext Db { get; } = new(new DbContextOptionsBuilder<ItemDelegationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public ItemDelegationService Service { get; }
        public DelegationFixture() => Service = new ItemDelegationService(new ItemDelegationRepository(Db), Db);

        public async Task<ItemDelegationDto> Hand(Guid item, Guid from, Guid to, DelegatedItemType type = DelegatedItemType.WaterfallActivity)
        {
            var result = await Service.DelegateAsync(Tenant, from, new DelegateItemRequest(type, item, Guid.NewGuid(), to), default);
            Assert.True(result.IsSuccess, result.Error.Message);
            return result.Value!;
        }
    }

    [Fact]
    public async Task HandingAnItemOver_IsRecordedAndListedForBothSides()
    {
        var f = new DelegationFixture();
        var item = Guid.NewGuid();

        var made = await f.Hand(item, Alice, Bob);

        Assert.True(made.IsActive);
        Assert.Equal(Alice, made.DelegatorUserId);
        Assert.Single((await f.Service.ListMineAsync(Tenant, Alice, true, default)).Value!);
        Assert.Single((await f.Service.ListMineAsync(Tenant, Bob, true, default)).Value!);
        Assert.Empty((await f.Service.ListMineAsync(Tenant, Carol, true, default)).Value!);
        Assert.Empty((await f.Service.ListMineAsync(Guid.NewGuid(), Alice, true, default)).Value!);
    }

    [Fact]
    public async Task DelegateRefuses_Yourself_NoOne_UnknownKinds_ADoubleHandOver_AndALoop()
    {
        var f = new DelegationFixture();
        var item = Guid.NewGuid();
        async Task<string> Code(Guid by, DelegateItemRequest request) => (await f.Service.DelegateAsync(Tenant, by, request, default)).Error.Code;

        Assert.Equal("validation.error", await Code(Alice, new DelegateItemRequest(DelegatedItemType.Action, item, null, Alice)));
        Assert.Equal("validation.error", await Code(Alice, new DelegateItemRequest(DelegatedItemType.Action, item, null, Guid.Empty)));
        Assert.Equal("validation.error", await Code(Alice, new DelegateItemRequest(DelegatedItemType.Action, Guid.Empty, null, Bob)));
        Assert.Equal("validation.error", await Code(Alice, new DelegateItemRequest((DelegatedItemType)99, item, null, Bob)));

        await f.Hand(item, Alice, Bob, DelegatedItemType.Action);
        Assert.Equal("conflict", await Code(Alice, new DelegateItemRequest(DelegatedItemType.Action, item, null, Carol)));
        Assert.Equal("conflict", await Code(Bob, new DelegateItemRequest(DelegatedItemType.Action, item, null, Alice)));
        // the same people for a different item, or a different kind, are unaffected
        await f.Hand(item, Alice, Bob, DelegatedItemType.AgileTask);
    }

    [Fact]
    public async Task EndingAHandOver_EndsEverythingPassedOnAfterIt_AndSaysWhoGetsTheItemBack()
    {
        var f = new DelegationFixture();
        var item = Guid.NewGuid();
        var first = await f.Hand(item, Alice, Bob);   // A -> B
        var second = await f.Hand(item, Bob, Carol);  // B -> C
        var third = await f.Hand(item, Carol, Dave);  // C -> D
        var unrelated = await f.Hand(Guid.NewGuid(), Alice, Bob);

        var ended = (await f.Service.EndAsync(Tenant, second.Id, Bob, false, default)).Value!;

        Assert.Equal(Bob, ended.RestoreToUserId); // B takes it back from C
        Assert.Equal(new[] { second.Id, third.Id }.Order(), ended.EndedIds.Order());
        var active = (await f.Service.ListMineAsync(Tenant, Alice, true, default)).Value!;
        Assert.Equal(new[] { first.Id, unrelated.Id }.Order(), active.Select(d => d.Id).Order());
        var all = (await f.Service.ListMineAsync(Tenant, Dave, false, default)).Value!;
        Assert.False(Assert.Single(all).IsActive);
        Assert.NotNull(all[0].EndedAtUtc);
    }

    [Fact]
    public async Task EndingAHandOver_NeedsTheDelegatorOrAnAdministrator_AndOnlyOnce()
    {
        var f = new DelegationFixture();
        var made = await f.Hand(Guid.NewGuid(), Alice, Bob);

        Assert.Equal("forbidden", (await f.Service.EndAsync(Tenant, made.Id, Bob, false, default)).Error.Code);
        Assert.Equal("not_found", (await f.Service.EndAsync(Tenant, Guid.NewGuid(), Alice, false, default)).Error.Code);
        Assert.Equal("not_found", (await f.Service.EndAsync(Guid.NewGuid(), made.Id, Alice, true, default)).Error.Code);
        Assert.True((await f.Service.EndAsync(Tenant, made.Id, Carol, true, default)).IsSuccess);
        Assert.Equal("conflict", (await f.Service.EndAsync(Tenant, made.Id, Alice, false, default)).Error.Code);
    }

    [Fact]
    public async Task AChainThatLoopsInTheData_StillEnds()
    {
        var f = new DelegationFixture();
        var item = Guid.NewGuid();
        var first = new ItemDelegation(Guid.NewGuid(), Tenant, DelegatedItemType.Action, item, null, Alice, Bob);
        var second = new ItemDelegation(Guid.NewGuid(), Tenant, DelegatedItemType.Action, item, null, Bob, Alice);
        f.Db.ItemDelegations.AddRange(first, second);
        await f.Db.SaveChangesAsync();

        var ended = (await f.Service.EndAsync(Tenant, first.Id, Alice, false, default)).Value!;

        Assert.Equal(2, ended.EndedIds.Count);
    }

    // -------------------------------------------------------------- schema

    private static ItemDelegationDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<ItemDelegationDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheDelegationScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "item_delegation", "2026-10-15-add-item-delegation.sql", typeof(ItemDelegationSchemaUpgrade), ["ItemDelegations"]);
    }

    [Fact]
    public void TheDelegationUpgrade_OnlyAddsThings()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-15-add-item-delegation.sql", typeof(ItemDelegationSchemaUpgrade));
    }
}
