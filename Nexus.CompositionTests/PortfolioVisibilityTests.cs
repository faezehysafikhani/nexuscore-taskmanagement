using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.Integrations.ProjectVisibility.Application;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using Nexus.Portfolio.Application;
using Nexus.Portfolio.Application.Dtos;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Team.Application;
using Nexus.ProjectManagement.Team.Domain;
using Nexus.Workflow.Application;
using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

public sealed class PortfolioVisibilityTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();

    // ----------------------------------------------------------------- fakes

    private sealed class FakeUnits(params OrganizationUnit[] units) : IOrganizationUnitRepository
    {
        public Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(units.SingleOrDefault(u => u.Id == id));
        public Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrganizationUnit>>(units);
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(OrganizationUnit unit, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeTeam(params ProjectMember[] members) : ITeamRepository
    {
        public Task<ProjectMember?> GetMemberByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsMemberAsync(Guid projectId, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListProjectIdsForUserAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(members.Where(m => m.TenantId == tenantId && m.UserId == userId).Select(m => m.ProjectId).Distinct().ToList());
        public Task AddMemberAsync(ProjectMember member, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveMemberAsync(ProjectMember member, CancellationToken ct) => throw new NotSupportedException();
        public Task<GovernanceRole?> GetGovernanceRoleByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GovernanceRole>> ListGovernanceRolesAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddGovernanceRoleAsync(GovernanceRole role, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveGovernanceRoleAsync(GovernanceRole role, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeWorkflow(Result<IReadOnlyList<WorkflowInstanceDto>> pending) : IWorkflowInstanceService
    {
        public Task<Result<IReadOnlyList<WorkflowInstanceDto>>> ListPendingForApproverAsync(Guid tenantId, Guid approverUserId, CancellationToken ct) =>
            Task.FromResult(approverUserId == Alice ? pending : Result.Success<IReadOnlyList<WorkflowInstanceDto>>([]));
        public Task<Result<WorkflowInstanceDto>> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<WorkflowInstanceDto>> ApproveAsync(Guid id, Guid by, DecideWorkflowInstanceRequest r, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<WorkflowInstanceDto>> RejectAsync(Guid id, Guid by, DecideWorkflowInstanceRequest r, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeProjects(params Project[] projects) : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(projects.SingleOrDefault(p => p.Id == id));
        public Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken ct) =>
            Task.FromResult(new PagedResult<Project>(projects, request.PageNumber, request.PageSize, projects.Length));
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(Project project, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeActions(params ActionItem[] actions) : IActionItemRepository
    {
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(actions.SingleOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ActionItem>>(actions);
        public Task AddAsync(ActionItem action, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedProvider(VisibilityGrant grant) : IVisibilityProvider
    {
        public int Calls { get; private set; }
        public Guid? AskedAbout { get; private set; }
        public Task<VisibilityGrant> GetGrantAsync(Guid tenantId, Guid userId, CancellationToken ct)
        {
            Calls++;
            AskedAbout = userId;
            return Task.FromResult(grant);
        }
    }

    private static OrganizationUnit Unit(string name, Guid? parent = null, Guid? manager = null)
    {
        var unit = new OrganizationUnit(Guid.NewGuid(), Tenant, name, name, parent);
        unit.Update(name, name, parent, manager, true);
        return unit;
    }

    private static Project NewProject(string code, Guid? owner = null, Guid? manager = null, Guid? unit = null)
    {
        var project = new Project(Guid.NewGuid(), Tenant, code, code, ProjectType.Waterfall, manager, owner);
        project.UpdateDetails(code, code, manager, owner, unit, null, null, null, null, null, null, null, null, null, null);
        return project;
    }

    private static ActionItem NewAction(string title, Guid? owner = null, Guid? responsible = null, Guid? unit = null, Guid? project = null)
    {
        var action = new ActionItem(Guid.NewGuid(), Tenant, title, unit ?? Guid.NewGuid(), Guid.NewGuid(), project);
        action.UpdateDetails(title, null, owner, responsible, unit ?? Guid.NewGuid(), Guid.NewGuid(), project, null, null);
        return action;
    }

    private static WorkflowInstanceDto Pending(string subjectType, Guid subjectId, WorkflowInstanceStatus status = WorkflowInstanceStatus.InProgress) =>
        new(Guid.NewGuid(), Tenant, Guid.NewGuid(), subjectType, subjectId, 1, 1, status, []);

    // -------------------------------------------------------------- providers

    [Fact]
    public async Task TeamMembers_SeeTheProjectsTheyAreOn_AndOnlyThose()
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var provider = new TeamMembershipVisibilityProvider(new FakeTeam(
            new ProjectMember(Guid.NewGuid(), Tenant, p1, Alice), new ProjectMember(Guid.NewGuid(), Tenant, p2, Alice),
            new ProjectMember(Guid.NewGuid(), Tenant, p2, Bob), new ProjectMember(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Alice)));

        var grant = await provider.GetGrantAsync(Tenant, Alice, default);

        Assert.Equal(new[] { p1, p2 }.Order(), grant.ProjectIds.Order());
        Assert.Empty(grant.ActionIds);
        Assert.Empty(grant.OrganizationUnitIds);
        Assert.Equal([p2], (await provider.GetGrantAsync(Tenant, Bob, default)).ProjectIds);
        Assert.Empty((await provider.GetGrantAsync(Tenant, Carol, default)).ProjectIds);
    }

    [Fact]
    public async Task AUnitManager_SeesTheWholeBranchBelowTheirUnit_NotTheRest()
    {
        var root = Unit("Head office", manager: Alice);
        var engineering = Unit("Engineering", root.Id);
        var civil = Unit("Civil", engineering.Id, manager: Bob);
        var finance = Unit("Finance", root.Id);
        var other = Unit("Other root");
        var provider = new OrganizationUnitManagerVisibilityProvider(new FakeUnits(root, engineering, civil, finance, other));

        Assert.Equal(new[] { root.Id, engineering.Id, civil.Id, finance.Id }.Order(), (await provider.GetGrantAsync(Tenant, Alice, default)).OrganizationUnitIds.Order());
        Assert.Equal(new[] { civil.Id }, (await provider.GetGrantAsync(Tenant, Bob, default)).OrganizationUnitIds);
        Assert.Empty((await provider.GetGrantAsync(Tenant, Carol, default)).OrganizationUnitIds);
    }

    [Fact]
    public async Task AUnitManager_WithALoopingChart_StillGetsAnAnswer()
    {
        var a = Unit("A", manager: Alice);
        var b = Unit("B", a.Id);
        a.Update("A", "A", b.Id, Alice, true); // A's parent is B, B's parent is A

        var grant = await new OrganizationUnitManagerVisibilityProvider(new FakeUnits(a, b)).GetGrantAsync(Tenant, Alice, default);

        Assert.Equal(new[] { a.Id, b.Id }.Order(), grant.OrganizationUnitIds.Order());
    }

    [Fact]
    public async Task Approvers_SeeTheProjectsAndActionsWaitingForThem_NotOtherSubjectsOrFinishedOnes()
    {
        var project = Guid.NewGuid();
        var action = Guid.NewGuid();
        var provider = new PendingApprovalVisibilityProvider(new FakeWorkflow(Result.Success<IReadOnlyList<WorkflowInstanceDto>>(
        [
            Pending("Project", project), Pending("Action", action), Pending("Risk", Guid.NewGuid()),
            Pending("Project", Guid.NewGuid(), WorkflowInstanceStatus.Approved)
        ])));

        var grant = await provider.GetGrantAsync(Tenant, Alice, default);

        Assert.Equal([project], grant.ProjectIds);
        Assert.Equal([action], grant.ActionIds);
        Assert.Empty((await provider.GetGrantAsync(Tenant, Bob, default)).ProjectIds);
    }

    [Fact]
    public async Task Approvers_AFailingWorkflowLookup_GrantsNothingInsteadOfFailing()
    {
        var provider = new PendingApprovalVisibilityProvider(new FakeWorkflow(Result.Failure<IReadOnlyList<WorkflowInstanceDto>>(Error.Validation("broken"))));

        var grant = await provider.GetGrantAsync(Tenant, Alice, default);

        Assert.Empty(grant.ProjectIds);
        Assert.Empty(grant.ActionIds);
    }

    [Fact]
    public async Task AMissingModule_GrantsNothing()
    {
        Assert.Empty((await new TeamMembershipVisibilityProvider().GetGrantAsync(Tenant, Alice, default)).ProjectIds);
        Assert.Empty((await new OrganizationUnitManagerVisibilityProvider().GetGrantAsync(Tenant, Alice, default)).OrganizationUnitIds);
        Assert.Empty((await new PendingApprovalVisibilityProvider().GetGrantAsync(Tenant, Alice, default)).ProjectIds);
    }

    [Fact]
    public void TheUnionOfGrants_IsEverythingAnyOfThemAllows()
    {
        var p = Guid.NewGuid();
        var q = Guid.NewGuid();
        var union = VisibilityGrant.Union(
        [
            new VisibilityGrant(new HashSet<Guid> { p }, new HashSet<Guid>(), new HashSet<Guid>()),
            new VisibilityGrant(new HashSet<Guid> { p, q }, new HashSet<Guid>(), new HashSet<Guid>())
        ]);

        Assert.Equal(new[] { p, q }.Order(), union.ProjectIds.Order());
        Assert.Empty(VisibilityGrant.Union([]).ProjectIds);
    }

    // --------------------------------------------------------------- portfolio

    private static PortfolioQuery Query(Guid user, bool viewAll = false, string? type = null) =>
        new(Tenant, user, viewAll, OrganizationUnitId: null, Status: null, Search: null, InvolvedUserId: null, Type: type, ApprovalStatus: null, Priority: null);

    private static async Task<PortfolioResultDto> RunAsync(PortfolioService service, PortfolioQuery query) =>
        (await service.GetPortfolioAsync(query, default)).Value!;

    [Fact]
    public async Task WithoutProviders_AUserSeesOnlyWhatTheyOwnManageOrAreResponsibleFor()
    {
        var mine = NewProject("MINE", manager: Alice);
        var theirs = NewProject("THEIRS", manager: Bob);
        var myAction = NewAction("Mine", responsible: Alice);
        var theirAction = NewAction("Theirs", responsible: Bob);

        var result = await RunAsync(new PortfolioService(new FakeProjects(mine, theirs), new FakeActions(myAction, theirAction)), Query(Alice));

        Assert.Equal(["MINE"], result.Projects.Select(p => p.Code));
        Assert.Equal(["Mine"], result.Actions.Select(a => a.Title));
    }

    [Fact]
    public async Task AGrantedProject_AppearsWithItsActions_WhoeverOwnsThem()
    {
        var mine = NewProject("MINE", manager: Alice);
        var onTeam = NewProject("TEAM", manager: Bob);
        var unrelated = NewProject("OTHER", manager: Bob);
        var teamAction = NewAction("In team project", responsible: Bob, project: onTeam.Id);
        var otherAction = NewAction("In other project", responsible: Bob, project: unrelated.Id);
        var provider = new FixedProvider(new VisibilityGrant(new HashSet<Guid> { onTeam.Id }, new HashSet<Guid>(), new HashSet<Guid>()));
        var service = new PortfolioService(new FakeProjects(mine, onTeam, unrelated), new FakeActions(teamAction, otherAction), [provider]);

        var result = await RunAsync(service, Query(Alice));

        Assert.Equal(["MINE", "TEAM"], result.Projects.Select(p => p.Code).Order());
        Assert.Equal(["In team project"], result.Actions.Select(a => a.Title));
        Assert.Equal(Alice, provider.AskedAbout);
    }

    [Fact]
    public async Task AGrantedUnit_ShowsItsProjectsAndActions()
    {
        var unit = Guid.NewGuid();
        var inUnit = NewProject("IN", manager: Bob, unit: unit);
        var outside = NewProject("OUT", manager: Bob, unit: Guid.NewGuid());
        var unitAction = NewAction("Unit action", responsible: Bob, unit: unit);
        var elsewhere = NewAction("Elsewhere", responsible: Bob, unit: Guid.NewGuid());
        var provider = new FixedProvider(new VisibilityGrant(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid> { unit }));
        var service = new PortfolioService(new FakeProjects(inUnit, outside), new FakeActions(unitAction, elsewhere), [provider]);

        var result = await RunAsync(service, Query(Alice));

        Assert.Equal(["IN"], result.Projects.Select(p => p.Code));
        Assert.Equal(["Unit action"], result.Actions.Select(a => a.Title));
    }

    [Fact]
    public async Task AGrantedAction_AppearsOnItsOwn()
    {
        var action = NewAction("Needs my approval", responsible: Bob);
        var other = NewAction("Not mine", responsible: Bob);
        var provider = new FixedProvider(new VisibilityGrant(new HashSet<Guid>(), new HashSet<Guid> { action.Id }, new HashSet<Guid>()));
        var service = new PortfolioService(new FakeProjects(), new FakeActions(action, other), [provider]);

        Assert.Equal(["Needs my approval"], (await RunAsync(service, Query(Alice))).Actions.Select(a => a.Title));
    }

    [Fact]
    public async Task SeveralProviders_AreCombined()
    {
        var a = NewProject("A", manager: Bob);
        var b = NewProject("B", manager: Bob);
        var service = new PortfolioService(new FakeProjects(a, b), new FakeActions(),
        [
            new FixedProvider(new VisibilityGrant(new HashSet<Guid> { a.Id }, new HashSet<Guid>(), new HashSet<Guid>())),
            new FixedProvider(new VisibilityGrant(new HashSet<Guid> { b.Id }, new HashSet<Guid>(), new HashSet<Guid>()))
        ]);

        Assert.Equal(2, (await RunAsync(service, Query(Alice))).Projects.Count);
    }

    [Fact]
    public async Task ViewAll_SeesEverything_WithoutAskingTheProviders()
    {
        var provider = new FixedProvider(VisibilityGrant.None);
        var service = new PortfolioService(new FakeProjects(NewProject("A", manager: Bob), NewProject("B", manager: Bob)), new FakeActions(NewAction("x")), [provider]);

        var result = await RunAsync(service, Query(Alice, viewAll: true));

        Assert.Equal(2, result.Projects.Count);
        Assert.Single(result.Actions);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Filters_StillNarrowWhatAGrantShows()
    {
        var team = NewProject("TEAM", manager: Bob);
        var action = NewAction("Granted action", responsible: Bob, project: team.Id);
        var provider = new FixedProvider(new VisibilityGrant(new HashSet<Guid> { team.Id }, new HashSet<Guid>(), new HashSet<Guid>()));
        var service = new PortfolioService(new FakeProjects(team), new FakeActions(action), [provider]);

        var onlyActions = await RunAsync(service, Query(Alice, type: "Action"));

        Assert.Empty(onlyActions.Projects);
        Assert.Single(onlyActions.Actions);
    }

    [Fact]
    public async Task ARealIntegration_TeamMembershipAndAnApproverGrant_WorkThroughThePortfolio()
    {
        var teamProject = NewProject("TEAM", manager: Bob);
        var approvalProject = NewProject("APPROVE", manager: Bob);
        var hidden = NewProject("HIDDEN", manager: Bob);
        var providers = new IVisibilityProvider[]
        {
            new TeamMembershipVisibilityProvider(new FakeTeam(new ProjectMember(Guid.NewGuid(), Tenant, teamProject.Id, Alice))),
            new PendingApprovalVisibilityProvider(new FakeWorkflow(Result.Success<IReadOnlyList<WorkflowInstanceDto>>([Pending("Project", approvalProject.Id)])))
        };
        var service = new PortfolioService(new FakeProjects(teamProject, approvalProject, hidden), new FakeActions(), providers);

        Assert.Equal(["APPROVE", "TEAM"], (await RunAsync(service, Query(Alice))).Projects.Select(p => p.Code).Order());
        Assert.Empty((await RunAsync(service, Query(Carol))).Projects);
    }
}
