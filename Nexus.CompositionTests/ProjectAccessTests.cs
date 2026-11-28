using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Core.Infrastructure;
using Nexus.ProjectManagement.Core.Permissions;
using Nexus.ProjectManagement.RiskManagement.Domain;
using Nexus.ProjectManagement.RiskManagement.Infrastructure;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>Phase 5 (M7): another organization's items are not reachable by id; only the manager, the owner or Projects.ManageAll manage a project.</summary>
public sealed class ProjectAccessTests
{
    private static readonly Guid Ours = Guid.NewGuid();
    private static readonly Guid Theirs = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();

    private sealed class SignedIn(Guid user, Guid tenant, params string[] permissions) : ICurrentUserContext
    {
        public Guid? UserId => user;
        public Guid? TenantId => tenant;
        public string? Email => null;
        public string? IpAddress => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }

    private sealed class NoApproval : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private static ProjectManagementCoreDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<ProjectManagementCoreDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<(string Store, Project Project)> SeedAsync()
    {
        var store = Guid.NewGuid().ToString();
        await using var db = NewDb(store);
        var project = new Project(Guid.NewGuid(), Ours, "پروژه", "P1", ProjectType.Waterfall, Manager, Owner);
        db.Add(project);
        await db.SaveChangesAsync();
        return (store, project);
    }

    private static ProjectService ServiceFor(ProjectManagementCoreDbContext db, ICurrentUserContext user) =>
        new(new ProjectRepository(db, user), db, new NoApproval(), currentUser: user);

    [Fact]
    public async Task AnotherTenant_CannotReadEditSendOrMoveTheProject_ById()
    {
        var (store, project) = await SeedAsync();
        await using var db = NewDb(store);
        var stranger = ServiceFor(db, new SignedIn(Manager, Theirs, ProjectPermissions.ManageAll)); // even with every right, in their own tenant

        Assert.Equal("not_found", (await stranger.GetAsync(project.Id, default)).Error.Code);
        Assert.Equal("not_found", (await stranger.SubmitForApprovalAsync(project.Id, default)).Error.Code);
        Assert.Equal("not_found", (await stranger.ChangeStatusAsync(project.Id, new ChangeProjectStatusRequest(ProjectStatus.Draft), default)).Error.Code);
    }

    [Fact]
    public async Task OnlyTheManagerTheOwnerOrManageAll_EditSendAndMoveAProject()
    {
        var (store, project) = await SeedAsync();
        var edit = new UpdateProjectRequest("پروژه", "P1", Manager, Owner, null, null, null, null, null, null, null, null, null, null, "تغییر");

        await using (var db = NewDb(store))
        {
            var colleague = ServiceFor(db, new SignedIn(Colleague, Ours));
            Assert.True((await colleague.GetAsync(project.Id, default)).IsSuccess); // seeing is not managing
            Assert.Equal("project.forbidden", (await colleague.UpdateAsync(project.Id, edit, default)).Error.Code);
            Assert.Equal("project.forbidden", (await colleague.SubmitForApprovalAsync(project.Id, default)).Error.Code);
            Assert.Equal("project.forbidden", (await colleague.ChangeStatusAsync(project.Id, new ChangeProjectStatusRequest(ProjectStatus.Draft), default)).Error.Code);
        }

        foreach (var user in new ICurrentUserContext[] { new SignedIn(Manager, Ours), new SignedIn(Owner, Ours), new SignedIn(Colleague, Ours, ProjectPermissions.ManageAll) })
        {
            await using var db = NewDb(store);
            Assert.True((await ServiceFor(db, user).UpdateAsync(project.Id, edit, default)).IsSuccess);
        }
    }

    [Fact]
    public async Task ARegisterItemOfAnotherTenant_IsNotFoundById()
    {
        var store = Guid.NewGuid().ToString();
        DbContextOptions<RiskManagementDbContext> options = new DbContextOptionsBuilder<RiskManagementDbContext>().UseInMemoryDatabase(store).Options;
        var risk = new Risk(Guid.NewGuid(), Ours, Guid.NewGuid(), "ریسک", 3, 3, 3);
        await using (var db = new RiskManagementDbContext(options))
        {
            db.Add(risk);
            await db.SaveChangesAsync();
        }

        await using var read = new RiskManagementDbContext(options);
        Assert.Null(await new RiskRepository(read, new SignedIn(Manager, Theirs)).GetByIdAsync(risk.Id, default));
        Assert.NotNull(await new RiskRepository(read, new SignedIn(Manager, Ours)).GetByIdAsync(risk.Id, default));
        Assert.NotNull(await new RiskRepository(read).GetByIdAsync(risk.Id, default)); // no user (a background job): unrestricted
    }
}
