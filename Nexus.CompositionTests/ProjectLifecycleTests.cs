using Microsoft.EntityFrameworkCore;
using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.Portfolio.Application;
using Nexus.Portfolio.Application.Dtos;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

public sealed class ProjectLifecycleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();

    private sealed class Repo(params Project[] initial) : IProjectRepository
    {
        public List<Project> Items { get; } = [.. initial];
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(p => p.Id == id));
        public Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken ct) =>
            Task.FromResult(new PagedResult<Project>(Items.Where(p => p.TenantId == request.TenantId).ToList(), request.PageNumber, request.PageSize, Items.Count));
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) =>
            Task.FromResult(Items.Any(p => p.TenantId == tenantId && p.Code == code && p.Id != excludeId));
        public Task AddAsync(Project project, CancellationToken ct) { Items.Add(project); return Task.CompletedTask; }
        public Task RemoveAsync(Project project, CancellationToken ct) { Items.Remove(project); return Task.CompletedTask; }
    }

    private sealed class Uow : IProjectManagementUnitOfWork
    {
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { Saves++; return Task.FromResult(1); }
    }

    private sealed class NoApproval : NexusCore.Application.Approvals.IApprovalRequester
    {
        public Task<NexusCore.Application.Approvals.ApprovalRequestOutcome> RequestApprovalAsync(NexusCore.Application.Approvals.ApprovalSubject subject, CancellationToken ct) =>
            Task.FromResult(NexusCore.Application.Approvals.ApprovalRequestOutcome.NotConfigured);
    }

    private sealed class Participant(string? reason) : IProjectDeletionParticipant
    {
        public Guid? Deleted { get; private set; }
        public Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken ct) => Task.FromResult(reason);
        public Task OnProjectDeletedAsync(Guid tenantId, Guid projectId, CancellationToken ct) { Deleted = projectId; return Task.CompletedTask; }
    }

    private static ProjectService Service(Repo repo, Uow? uow = null, ProjectOptions? options = null, params IProjectDeletionParticipant[] participants) =>
        new(repo, uow ?? new Uow(), new NoApproval(), participants, options);

    private static CreateProjectRequest Create(string code = "P1", Guid? manager = null, Guid? unit = null, string? goal = "A real goal", string? requirements = "Real requirements",
        string? description = null, DateOnly? start = null, DateOnly? end = null) =>
        new(Tenant, "Project " + code, code, ProjectType.Waterfall, manager, Alice, unit, null,
            start ?? new DateOnly(2026, 1, 1), end ?? new DateOnly(2026, 6, 1), null, goal, requirements, null, null, description, null);

    private static Project ExistingProject(string code = "P1", Guid? owner = null, Guid? manager = null)
    {
        var project = new Project(Guid.NewGuid(), Tenant, code, code, ProjectType.Waterfall, manager, owner);
        project.UpdateDetails(code, code, manager, owner, Unit, null, null, null, null, null, null, null, null, null, null);
        return project;
    }

    // -------------------------------------------------------------- code

    [Fact]
    public async Task ABlankCode_GetsTheNextFreeAutomaticOne_AndAnExplicitCodeIsKept()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Nexus.ProjectManagement.Core.Infrastructure.ProjectManagementCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new Nexus.ProjectManagement.Core.Infrastructure.ProjectManagementCoreDbContext(options);
        var service = new ProjectService(new Nexus.ProjectManagement.Core.Infrastructure.ProjectRepository(db), db, new NoApproval());

        var first = await service.CreateAsync(Create(code: ""), default);
        var second = await service.CreateAsync(Create(code: ""), default);
        var explicitCode = await service.CreateAsync(Create(code: "ALPHA"), default);
        var third = await service.CreateAsync(Create(code: ""), default);

        Assert.Equal(["P0001", "P0002", "ALPHA", "P0003"], new[] { first, second, explicitCode, third }.Select(r => r.Value!.Code));
        Assert.Equal("conflict", (await service.CreateAsync(Create(code: "ALPHA"), default)).Error.Code);
    }

    // ------------------------------------------------------------ status

    [Fact]
    public async Task Status_CanMoveBetweenTheWorkingStatuses_ButNotToArchivedOrOutOfIt()
    {
        var project = ExistingProject();
        var service = Service(new Repo(project));

        Assert.Equal(ProjectStatus.OnHold, (await service.ChangeStatusAsync(project.Id, new ChangeProjectStatusRequest(ProjectStatus.OnHold), default)).Value!.Status);
        Assert.Equal(ProjectStatus.Completed, (await service.ChangeStatusAsync(project.Id, new ChangeProjectStatusRequest(ProjectStatus.Completed), default)).Value!.Status);

        Assert.Equal("validation.error", (await service.ChangeStatusAsync(project.Id, new ChangeProjectStatusRequest(ProjectStatus.Archived), default)).Error.Code);
        Assert.Equal("validation.error", (await service.ChangeStatusAsync(project.Id, new ChangeProjectStatusRequest((ProjectStatus)99), default)).Error.Code);
        Assert.Equal("not_found", (await service.ChangeStatusAsync(Guid.NewGuid(), new ChangeProjectStatusRequest(ProjectStatus.Active), default)).Error.Code);

        await service.ArchiveAsync(project.Id, default);
        Assert.Equal("conflict", (await service.ChangeStatusAsync(project.Id, new ChangeProjectStatusRequest(ProjectStatus.Active), default)).Error.Code);
    }

    // ------------------------------------------------------------ delete

    [Fact]
    public async Task Delete_WithNoParticipants_RemovesTheProject()
    {
        var project = ExistingProject();
        var repo = new Repo(project);
        var uow = new Uow();

        Assert.True((await Service(repo, uow).DeleteAsync(project.Id, default)).IsSuccess);

        Assert.Empty(repo.Items);
        Assert.Equal(1, uow.Saves);
        Assert.Equal("not_found", (await Service(repo).DeleteAsync(project.Id, default)).Error.Code);
    }

    [Fact]
    public async Task Delete_IsRefused_WhileAnyModuleStillHoldsDataAboutTheProject_AndSaysWhat()
    {
        var project = ExistingProject();
        var repo = new Repo(project);
        var blocking = new Participant("3 risk(s)");
        var other = new Participant("2 document(s)");
        var free = new Participant(null);

        var result = await Service(repo, null, null, blocking, free, other).DeleteAsync(project.Id, default);

        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("3 risk(s)", result.Error.Message);
        Assert.Contains("2 document(s)", result.Error.Message);
        Assert.Single(repo.Items);
        Assert.Null(blocking.Deleted); // nobody is told anything was deleted
    }

    [Fact]
    public async Task Delete_TellsEveryParticipant_OnceTheProjectIsGone()
    {
        var project = ExistingProject();
        var a = new Participant(null);
        var b = new Participant(null);

        Assert.True((await Service(new Repo(project), null, null, a, b).DeleteAsync(project.Id, default)).IsSuccess);

        Assert.Equal(project.Id, a.Deleted);
        Assert.Equal(project.Id, b.Deleted);
    }

    // A repository written before deletion existed: it implements none of the new members.
    private sealed class LegacyRepo(params Project[] projects) : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(projects.SingleOrDefault(p => p.Id == id));
        public Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(Project project, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task ALegacyRepository_StillCompilesAndWorksForEverythingElse()
    {
        var project = ExistingProject();
        var service = new ProjectService(new LegacyRepo(project), new Uow(), new NoApproval());

        Assert.True((await service.GetAsync(project.Id, default)).IsSuccess);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.DeleteAsync(project.Id, default));
    }

    [Fact]
    public async Task TheIntegrationParticipant_WithNoneOfItsModulesInstalled_ObjectsToNothingAndDoesNothing()
    {
        var participant = new Nexus.Integrations.ProjectDeletion.Application.ProjectDataDeletionParticipant();

        Assert.Null(await participant.GetBlockingReasonAsync(Tenant, Guid.NewGuid(), default));
        await participant.OnProjectDeletedAsync(Tenant, Guid.NewGuid(), default);
    }

    // ----------------------------------------------------------- charter

    [Fact]
    public async Task WithoutTheCharterOption_AnIncompleteProjectIsAccepted_AsItAlwaysWas()
    {
        var service = Service(new Repo());

        var result = await service.CreateAsync(Create(manager: null, unit: null, goal: null, requirements: null), default);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("manager", "project.charter.manager_required")]
    [InlineData("unit", "project.charter.unit_required")]
    [InlineData("goal", "project.charter.goal_required")]
    [InlineData("requirements", "project.charter.requirements_required")]
    public async Task WithTheCharterOption_EachMissingPartIsNamed(string missing, string code)
    {
        var service = Service(new Repo(), null, new ProjectOptions { ValidateCharter = true });
        var request = Create(
            manager: missing == "manager" ? null : Bob, unit: missing == "unit" ? null : Unit,
            goal: missing == "goal" ? " " : "A real goal", requirements: missing == "requirements" ? null : "Real requirements");

        var result = await service.CreateAsync(request, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error.Code);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("تست")]
    [InlineData("abc")]
    [InlineData("1234")]
    public async Task WithTheCharterOption_MeaninglessText_IsRefused(string text)
    {
        var service = Service(new Repo(), null, new ProjectOptions { ValidateCharter = true });

        var result = await service.CreateAsync(Create(manager: Bob, unit: Unit, goal: text), default);

        Assert.Equal("project.charter.goal_meaningless", result.Error.Code);
    }

    [Fact]
    public async Task WithTheCharterOption_ACompleteCharterIsAccepted_AndUpdatesAreChecked()
    {
        var repo = new Repo();
        var service = Service(repo, null, new ProjectOptions { ValidateCharter = true });

        var created = await service.CreateAsync(Create(manager: Bob, unit: Unit, description: "Builds the new data centre"), default);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Message : null);

        var update = new UpdateProjectRequest("Renamed", "P1", null, Alice, Unit, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), null, "A real goal", "Real requirements", null, null, null, null);
        Assert.Equal("project.charter.manager_required", (await service.UpdateAsync(created.Value!.Id, update, default)).Error.Code);
        Assert.Equal(Bob, (await service.GetAsync(created.Value.Id, default)).Value!.ManagerUserId); // refused update changed nothing
    }

    // ---------------------------------------------------- visible projects

    [Fact]
    public async Task VisibleProjects_AreTheCallersOwnAndManaged_PlusWhatProvidersGrant_WithoutArchivedOnes()
    {
        var mine = ExistingProject("MINE", owner: Alice);
        var managed = ExistingProject("MANAGED", manager: Alice);
        var theirs = ExistingProject("THEIRS", owner: Bob);
        var granted = ExistingProject("GRANTED", owner: Bob);
        var archived = ExistingProject("OLD", owner: Alice);
        archived.Archive();
        var service = new PortfolioService(new Repo(mine, managed, theirs, granted, archived), new NoActions(),
            [new GrantProjects(granted.Id)]);

        var result = await service.GetVisibleProjectsAsync(new PortfolioQuery(Tenant, Alice, false, null, null), includeArchived: false, default);

        Assert.Equal(["GRANTED", "MANAGED", "MINE"], result.Value!.Select(p => p.Code).Order());
        var withArchived = await service.GetVisibleProjectsAsync(new PortfolioQuery(Tenant, Alice, false, null, null), includeArchived: true, default);
        Assert.Contains(withArchived.Value!, p => p.Code == "OLD");

        var everything = await service.GetVisibleProjectsAsync(new PortfolioQuery(Tenant, Bob, ViewAll: true, null, null), includeArchived: false, default);
        Assert.Equal(4, everything.Value!.Count);
    }

    private sealed class GrantProjects(params Guid[] ids) : IVisibilityProvider
    {
        public Task<VisibilityGrant> GetGrantAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
            Task.FromResult(new VisibilityGrant(ids.ToHashSet(), new HashSet<Guid>(), new HashSet<Guid>()));
    }

    private sealed class NoActions : IActionItemRepository
    {
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<ActionItem?>(null);
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ActionItem>>([]);
        public Task AddAsync(ActionItem action, CancellationToken ct) => Task.CompletedTask;
    }
}
