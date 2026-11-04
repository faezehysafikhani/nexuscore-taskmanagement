using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using NexusCore.Application.Identity.Interfaces;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>
/// Where a product limits whom a user may make responsible (IWorkAssignmentScope), the task
/// endpoints refuse anyone else as a new responsible person - and keep whoever a task already has.
/// </summary>
public sealed class TaskAssignmentLimitTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] Permissions =
        [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.Assign];

    [Fact]
    public async Task ANewTask_CannotBeGivenToSomeoneAboveTheUser_ButToAnyoneElse()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var manager = await host.UserAsync(host.TenantA, Permissions);
        var colleague = await host.UserAsync(host.TenantA, Permissions);
        host.AssignmentScope.Block(owner.Id, manager.Id);

        var refused = await CreateAsync(owner, manager.Id);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("own level or below", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await CreateAsync(owner, colleague.Id)).StatusCode);

        // Giving it to someone above along with an allowed person is refused too.
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(owner, colleague.Id, manager.Id)).StatusCode);
    }

    [Fact]
    public async Task ATaskKeepsItsResponsiblePeople_OnlyNewOnesAreChecked()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var later = await host.UserAsync(host.TenantA, Permissions);
        var other = await host.UserAsync(host.TenantA, Permissions);
        var created = await CreateAsync(owner, later.Id);
        var taskId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        // Their place changes after the task was given to them: they stay on it, edits still work.
        host.AssignmentScope.Block(owner.Id, later.Id, other.Id);
        Assert.True((await host.SendAsync(owner, HttpMethod.Put, $"/api/task-management/tasks/{taskId}", new
        {
            title = "Renamed", dueDate = "2030-01-01", priority = "High", responsibleUserIds = new[] { later.Id },
        })).IsSuccessStatusCode);

        // But nobody blocked can be added - by edit or by reassignment.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(owner, HttpMethod.Put, $"/api/task-management/tasks/{taskId}", new
        {
            title = "Renamed", dueDate = "2030-01-01", priority = "High", responsibleUserIds = new[] { later.Id, other.Id },
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(owner, HttpMethod.Patch, $"/api/task-management/tasks/{taskId}/assigned-user", new
        {
            assignedUserId = other.Id, responsibleUserIds = new[] { other.Id },
        })).StatusCode);
    }

    [Fact]
    public async Task TheTaskScreen_LearnsWhomTheUserMayNotChoose()
    {
        var free = await host.UserAsync(host.TenantA, Permissions);
        var limited = await host.UserAsync(host.TenantA, Permissions);
        var above = await host.UserAsync(host.TenantA, Permissions);
        host.AssignmentScope.Block(limited.Id, above.Id);

        using var none = JsonDocument.Parse(await (await host.SendAsync(free, HttpMethod.Get, "/api/task-management/assignment-limit")).Content.ReadAsStringAsync());
        Assert.False(none.RootElement.GetProperty("limited").GetBoolean());
        Assert.Equal(0, none.RootElement.GetProperty("blockedUserIds").GetArrayLength());

        using var some = JsonDocument.Parse(await (await host.SendAsync(limited, HttpMethod.Get, "/api/task-management/assignment-limit")).Content.ReadAsStringAsync());
        Assert.True(some.RootElement.GetProperty("limited").GetBoolean());
        Assert.Equal(above.Id, Assert.Single(some.RootElement.GetProperty("blockedUserIds").EnumerateArray()).GetGuid());
    }

    private Task<HttpResponseMessage> CreateAsync(AccessControlTests.Caller owner, params Guid[] responsible) =>
        host.SendAsync(owner, HttpMethod.Post, "/api/task-management/tasks", new
        {
            title = "Report", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = responsible,
        });
}

/// <summary>A stand-in for a product's limit: nobody is limited until a test blocks someone for a user.</summary>
public sealed class ProgrammableAssignmentScope : IWorkAssignmentScope
{
    private readonly ConcurrentDictionary<Guid, HashSet<Guid>> _blocked = new();

    public void Block(Guid assigner, params Guid[] people) => _blocked[assigner] = [.. people];

    public Task<WorkAssignmentLimit?> GetLimitAsync(Guid tenantId, Guid assignerUserId, CancellationToken cancellationToken) =>
        Task.FromResult(_blocked.TryGetValue(assignerUserId, out var blocked) ? new WorkAssignmentLimit(blocked) : null);
}
