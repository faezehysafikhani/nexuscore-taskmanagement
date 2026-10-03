using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.Portfolio.Application;
using Nexus.Portfolio.Application.Dtos;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

public sealed class PortfolioFilterTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();

    private sealed class FakeProjectRepository(params Project[] projects) : IProjectRepository
    {
        public ListProjectsRequest? LastRequest { get; private set; }

        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(projects.SingleOrDefault(p => p.Id == id));

        // Honours Search the way the real repository does (name or code contains, matched
        // case-insensitively as SQL Server's default collation does), so the service's
        // pass-through of Search is actually exercised.
        public Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken ct)
        {
            LastRequest = request;
            var items = projects
                .Where(p => string.IsNullOrWhiteSpace(request.Search) || p.Name.Contains(request.Search, StringComparison.OrdinalIgnoreCase) || p.Code.Contains(request.Search, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(new PagedResult<Project>(items, request.PageNumber, request.PageSize, items.Count));
        }

        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(Project project, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeActionRepository(params ActionItem[] actions) : IActionItemRepository
    {
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(actions.SingleOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActionItem>>(actions);
        public Task AddAsync(ActionItem action, CancellationToken ct) => Task.CompletedTask;
    }

    private static Project NewProject(string name, string code, ProjectType type, Guid? manager, Guid? owner = null)
    {
        var project = new Project(Guid.NewGuid(), Tenant, name, code, type, manager, owner);
        project.UpdateDetails(name, code, manager, owner, Unit, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            null, null, null, null, null, null, null);
        return project;
    }

    private static ActionItem NewAction(string title, Guid? owner, Guid? responsible)
    {
        var action = new ActionItem(Guid.NewGuid(), Tenant, title, Unit, Guid.NewGuid());
        action.UpdateDetails(title, null, owner, responsible, Unit, Guid.NewGuid(), null, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        return action;
    }

    private sealed record Fixture(PortfolioService Service, FakeProjectRepository Projects)
    {
        public Project Dam { get; init; } = null!;
        public Project Hrm { get; init; } = null!;
        public ActionItem Review { get; init; } = null!;
        public ActionItem Audit { get; init; } = null!;
    }

    private static Fixture Build()
    {
        var dam = NewProject("Dam construction", "DAM-1", ProjectType.Waterfall, Alice);
        var hrm = NewProject("HR system", "HRM-1", ProjectType.Agile, Bob);
        var review = NewAction("Review drawings", owner: Alice, responsible: Bob);
        var audit = NewAction("Safety audit", owner: Bob, responsible: Bob);
        var projects = new FakeProjectRepository(dam, hrm);
        var service = new PortfolioService(projects, new FakeActionRepository(review, audit));
        return new Fixture(service, projects) { Dam = dam, Hrm = hrm, Review = review, Audit = audit };
    }

    private static PortfolioQuery Query(Guid user, bool viewAll = true, string? search = null, Guid? involved = null,
        string? type = null, string? approval = null, string? status = null) =>
        new(Tenant, user, viewAll, OrganizationUnitId: null, status, search, involved, type, approval);

    private static async Task<PortfolioResultDto> RunAsync(Fixture fixture, PortfolioQuery query)
    {
        var result = await fixture.Service.GetPortfolioAsync(query, default);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task NoFilters_ReturnsEverything_AndCarriesTheDates()
    {
        var fixture = Build();

        var result = await RunAsync(fixture, Query(Admin));

        Assert.Equal(2, result.Projects.Count);
        Assert.Equal(2, result.Actions.Count);
        var dam = result.Projects.Single(p => p.Id == fixture.Dam.Id);
        Assert.Equal(new DateOnly(2026, 1, 1), dam.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 31), dam.EndDate);
        Assert.Equal(new DateOnly(2026, 3, 31), result.Actions.First().EndDate);
    }

    [Fact]
    public async Task Search_MatchesProjectNameOrCode_AndActionTitle_CaseInsensitively()
    {
        var fixture = Build();

        var byName = await RunAsync(fixture, Query(Admin, search: "dam"));
        Assert.Equal([fixture.Dam.Id], byName.Projects.Select(p => p.Id));
        Assert.Empty(byName.Actions);
        Assert.Equal("dam", fixture.Projects.LastRequest!.Search);

        var byCode = await RunAsync(fixture, Query(Admin, search: "HRM-1"));
        Assert.Equal([fixture.Hrm.Id], byCode.Projects.Select(p => p.Id));

        var byTitle = await RunAsync(fixture, Query(Admin, search: "SAFETY"));
        Assert.Empty(byTitle.Projects);
        Assert.Equal([fixture.Audit.Id], byTitle.Actions.Select(a => a.Id));

    }

    [Theory]
    [InlineData("Waterfall", 1, 0)]
    [InlineData("agile", 1, 0)]
    [InlineData("Action", 0, 2)]
    public async Task Type_NarrowsToProjectsOfThatKind_OrToActionsOnly(string type, int projects, int actions)
    {
        var result = await RunAsync(Build(), Query(Admin, type: type));

        Assert.Equal(projects, result.Projects.Count);
        Assert.Equal(actions, result.Actions.Count);
    }

    [Fact]
    public async Task InvolvedUser_MatchesOwnerOrManager_ForProjects_AndOwnerOrResponsible_ForActions()
    {
        var fixture = Build();

        var result = await RunAsync(fixture, Query(Admin, involved: Alice));

        Assert.Equal([fixture.Dam.Id], result.Projects.Select(p => p.Id));
        Assert.Equal([fixture.Review.Id], result.Actions.Select(a => a.Id));
    }

    [Fact]
    public async Task ApprovalStatus_FiltersBothProjectsAndActions()
    {
        var fixture = Build();
        fixture.Dam.MarkPendingApproval();
        fixture.Audit.Approve();

        var pending = await RunAsync(fixture, Query(Admin, approval: nameof(ApprovalStatus.PendingApproval)));
        Assert.Equal([fixture.Dam.Id], pending.Projects.Select(p => p.Id));
        Assert.Empty(pending.Actions);

        var approved = await RunAsync(fixture, Query(Admin, approval: nameof(ApprovalStatus.Approved)));
        Assert.Empty(approved.Projects);
        Assert.Equal([fixture.Audit.Id], approved.Actions.Select(a => a.Id));
    }

    [Fact]
    public async Task Filters_NeverWidenWhatANonViewAllUserMaySee()
    {
        var fixture = Build();

        // Alice has no ViewAll, so she only ever sees what she owns/manages/is responsible for.
        // Asking for Bob's items through InvolvedUserId narrows that set further; it must never
        // add to it. "Review drawings" is visible to Alice (she owns it) and involves Bob (he is
        // responsible), so it stays; Bob's own "Safety audit" and his project must not leak.
        var result = await RunAsync(fixture, Query(Alice, viewAll: false, involved: Bob));

        Assert.Empty(result.Projects);
        Assert.Equal([fixture.Review.Id], result.Actions.Select(a => a.Id));

        var own = await RunAsync(fixture, Query(Alice, viewAll: false));
        Assert.Equal([fixture.Dam.Id], own.Projects.Select(p => p.Id));
        Assert.Equal([fixture.Review.Id], own.Actions.Select(a => a.Id));
    }
}
