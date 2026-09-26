using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Infrastructure;
using Nexus.TaskManagement.Permissions;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.CompositionTests;

/// <summary>
/// A task's team and the people responsible for it are separate: a task has zero or one team
/// and one or more responsible people - from inside or outside the team - and an access list
/// that decides who else sees it. Through the real HTTP endpoints; what is saved is what the
/// next read (and the next user) gets.
/// </summary>
public sealed class TaskResponsiblePeopleTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] Permissions =
    [
        TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit,
        TaskManagementPermissions.Assign
    ];

    private static readonly string[] Viewer = [TaskManagementPermissions.View, TaskManagementPermissions.Edit];

    [Fact]
    public async Task OneResponsiblePerson_IsSavedAsBefore()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var doer = await host.UserAsync(host.TenantA, Viewer);

        var task = await CreateAsync(owner, new { title = "One", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = new[] { doer.Id } });

        Assert.Equal([doer.Id], ResponsibleIds(task));
        Assert.Equal(doer.Id, task.GetProperty("assignedUser").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task SeveralResponsiblePeople_FromInsideAndOutsideTheTeam_AllSeeAndWorkOnIt()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var inTeam = await host.UserAsync(host.TenantA, Viewer);
        var teammate = await host.UserAsync(host.TenantA, Viewer);
        var outsider = await host.UserAsync(host.TenantA, Viewer);
        var team = await host.TeamAsync(host.TenantA, inTeam.Id, teammate.Id);

        var task = await CreateAsync(owner, new
        {
            title = "Shared work", dueDate = "2030-01-01", priority = "Medium",
            assignedUserGroupId = team, responsibleUserIds = new[] { inTeam.Id, outsider.Id },
        });
        var id = task.GetProperty("id").GetGuid();

        Assert.Equal([inTeam.Id, outsider.Id], ResponsibleIds(task));
        Assert.Equal(team, task.GetProperty("assignedUserGroup").GetProperty("id").GetGuid());
        foreach (var person in new[] { inTeam, outsider })
        {
            Assert.Contains(id, await host.ListTaskIdsAsync(person));
            // Each responsible person may update the status, not only the first.
            Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(person, HttpMethod.Patch, $"/api/task-management/tasks/{id}/status", new { status = "InProgress" })).StatusCode);
        }

        // The team's other member is not responsible and was not given access.
        Assert.DoesNotContain(id, await host.ListTaskIdsAsync(teammate));

        // Filtering by a responsible person finds the task for any of them.
        var filtered = await Json(await host.SendAsync(owner, HttpMethod.Get, $"/api/task-management/tasks?assignedUserId={outsider.Id}&pageSize=200"));
        Assert.Contains(filtered.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task WithoutATeam_SeveralPeople_IncludingTheCreator_CanBeResponsible()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var colleague = await host.UserAsync(host.TenantA, Viewer);

        var task = await CreateAsync(owner, new { title = "Ours", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = new[] { owner.Id, colleague.Id, owner.Id } });

        Assert.Equal([owner.Id, colleague.Id], ResponsibleIds(task));
        Assert.Equal(JsonValueKind.Null, task.GetProperty("assignedUserGroup").ValueKind);
    }

    [Fact]
    public async Task ADisabledUser_OrAUserOfAnotherOrganization_CannotBeMadeResponsible()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var fine = await host.UserAsync(host.TenantA, Viewer);
        var disabled = await host.UserAsync(host.TenantA, Viewer);
        var stranger = await host.UserAsync(host.TenantB, Viewer);
        await SetActiveAsync(disabled.Id, false);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(owner, new { title = "X", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = new[] { fine.Id, disabled.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(owner, new { title = "X", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = new[] { fine.Id, stranger.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(owner, new { title = "X", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = Array.Empty<Guid>() })).StatusCode);

        // Nor added later, through either endpoint.
        var id = (await CreateAsync(owner, new { title = "Y", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = new[] { fine.Id } })).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await EditAsync(owner, id, [fine.Id, disabled.Id])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(owner, HttpMethod.Patch, $"/api/task-management/tasks/{id}/assigned-user", new { assignedUserId = disabled.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(owner, HttpMethod.Patch, $"/api/task-management/tasks/{id}/assigned-user", new { responsibleUserIds = new[] { stranger.Id } })).StatusCode);
        Assert.Equal([fine.Id], ResponsibleIds(await GetAsync(owner, id)));
    }

    [Fact]
    public async Task EditingTheResponsiblePeople_IsSavedOnTheFirstSave_AndWhoLeftLosesTheTask()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var first = await host.UserAsync(host.TenantA, Viewer);
        var second = await host.UserAsync(host.TenantA, Viewer);
        var third = await host.UserAsync(host.TenantA, Viewer);
        var id = (await CreateAsync(owner, new { title = "Rotating", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = new[] { first.Id, second.Id } })).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await EditAsync(owner, id, [second.Id, third.Id])).StatusCode);

        Assert.Equal([second.Id, third.Id], ResponsibleIds(await GetAsync(owner, id)));
        Assert.Contains(id, await host.ListTaskIdsAsync(third));
        Assert.DoesNotContain(id, await host.ListTaskIdsAsync(first));
    }

    [Fact]
    public async Task AnOlderClient_SendingOnlyAssignedUserId_KeepsOrReplacesTheResponsiblePeople()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var first = await host.UserAsync(host.TenantA, Viewer);
        var second = await host.UserAsync(host.TenantA, Viewer);
        var other = await host.UserAsync(host.TenantA, Viewer);
        var id = (await CreateAsync(owner, new { title = "Old client", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = new[] { first.Id, second.Id } })).GetProperty("id").GetGuid();

        // Naming one of them (as an older screen does when saving other fields) keeps them all.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(owner, HttpMethod.Put, $"/api/task-management/tasks/{id}", new { title = "Renamed", dueDate = "2030-01-01", priority = "Medium", assignedUserId = first.Id })).StatusCode);
        Assert.Equal([first.Id, second.Id], ResponsibleIds(await GetAsync(owner, id)));

        // Naming someone else makes them the only one.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(owner, HttpMethod.Patch, $"/api/task-management/tasks/{id}/assigned-user", new { assignedUserId = other.Id })).StatusCode);
        Assert.Equal([other.Id], ResponsibleIds(await GetAsync(owner, id)));
    }

    [Fact]
    public async Task TheAccessList_GivesAndTakesAwayTheTask_OnTheFirstSave()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var member = await host.UserAsync(host.TenantA, Viewer);
        var other = await host.UserAsync(host.TenantA, Viewer);
        var team = await host.TeamAsync(host.TenantA, member.Id, other.Id);
        var id = (await CreateAsync(owner, new
        {
            title = "Team work", dueDate = "2030-01-01", priority = "Medium",
            assignedUserGroupId = team, responsibleUserIds = new[] { owner.Id }, assigneeUserIds = new[] { member.Id },
        })).GetProperty("id").GetGuid();
        Assert.Contains(id, await host.ListTaskIdsAsync(member));
        Assert.DoesNotContain(id, await host.ListTaskIdsAsync(other));

        // Access moves from one member to the other with one save; a reload shows the same.
        Assert.Equal(HttpStatusCode.OK, (await EditAsync(owner, id, [owner.Id], access: [other.Id], team: team)).StatusCode);
        Assert.DoesNotContain(id, await host.ListTaskIdsAsync(member));
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(member, HttpMethod.Get, $"/api/task-management/tasks/{id}")).StatusCode);
        Assert.Contains(id, await host.ListTaskIdsAsync(other));
        var reloaded = await GetAsync(owner, id);
        Assert.Equal(new[] { owner.Id, other.Id }.Order(), reloaded.GetProperty("assignees").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).Order());
        Assert.Equal([owner.Id], ResponsibleIds(reloaded));
    }

    [Fact]
    public async Task TheAccessList_TakesOnlyActiveUsersOfTheSameOrganization()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var disabled = await host.UserAsync(host.TenantA, Viewer);
        var stranger = await host.UserAsync(host.TenantB, Viewer);
        await SetActiveAsync(disabled.Id, false);
        var id = (await CreateAsync(owner, new { title = "Access", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = new[] { owner.Id } })).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await EditAsync(owner, id, [owner.Id], access: [disabled.Id])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await EditAsync(owner, id, [owner.Id], access: [stranger.Id])).StatusCode);
        Assert.DoesNotContain(id, await host.ListTaskIdsAsync(stranger));
    }

    private static Guid[] ResponsibleIds(JsonElement task) =>
        task.GetProperty("responsibleUsers").EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ToArray();

    private Task<HttpResponseMessage> PostAsync(AccessControlTests.Caller caller, object body) =>
        host.SendAsync(caller, HttpMethod.Post, "/api/task-management/tasks", body);

    private async Task<JsonElement> CreateAsync(AccessControlTests.Caller caller, object body)
    {
        var response = await PostAsync(caller, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private Task<HttpResponseMessage> EditAsync(AccessControlTests.Caller caller, Guid id, Guid[] responsible, Guid[]? access = null, Guid? team = null) =>
        host.SendAsync(caller, HttpMethod.Put, $"/api/task-management/tasks/{id}", new
        {
            title = "Edited", dueDate = "2030-01-01", priority = "Medium",
            assignedUserGroupId = team, responsibleUserIds = responsible, assigneeUserIds = access,
        });

    private async Task<JsonElement> GetAsync(AccessControlTests.Caller caller, Guid id)
    {
        var response = await host.SendAsync(caller, HttpMethod.Get, $"/api/task-management/tasks/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private async Task SetActiveAsync(Guid userId, bool active)
    {
        using var scope = host.Services.CreateScope();
        var core = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
        (await core.Users.SingleAsync(u => u.Id == userId)).SetActive(active);
        await core.SaveChangesAsync();
        var tasks = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        (await tasks.Users.SingleAsync(u => u.Id == userId)).SetActive(active);
        await tasks.SaveChangesAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
