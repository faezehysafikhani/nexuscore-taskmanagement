using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>
/// Where a product keeps everyone to their own company (IUserReachScope), people outside it are
/// not listed, cannot be messaged, given a task or access to one, and a Tasks.ManageAll holder
/// sees only the tasks of their own people. Nobody is kept unless the product says so.
/// </summary>
public sealed class UserReachTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] Permissions =
        [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.Assign];

    [Fact]
    public async Task Tasks_GoOnlyToOnesOwnPeople_AndSoDoesAccess()
    {
        var me = await host.UserAsync(host.TenantA, Permissions);
        var colleague = await host.UserAsync(host.TenantA, Permissions);
        var outsider = await host.UserAsync(host.TenantA, Permissions);
        host.Reach.Keep(me.Id, [me.Id, colleague.Id], elsewhere: [outsider.Id]);

        var refused = await CreateAsync(me, [outsider.Id]);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("own company", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(me, [colleague.Id], access: [outsider.Id])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CreateAsync(me, [colleague.Id], access: [me.Id])).StatusCode);

        // The task screen is told whom it may offer.
        using var limit = JsonDocument.Parse(await (await host.SendAsync(me, HttpMethod.Get, "/api/task-management/assignment-limit")).Content.ReadAsStringAsync());
        Assert.False(limit.RootElement.GetProperty("limited").GetBoolean()); // no level limit here
        Assert.Equal(new[] { me.Id, colleague.Id }.Order(), limit.RootElement.GetProperty("allowedUserIds").EnumerateArray().Select(x => x.GetGuid()).Order());
        Assert.Contains(outsider.Id, limit.RootElement.GetProperty("blockedUserIds").EnumerateArray().Select(x => x.GetGuid()));

        // Someone not kept is told nothing is limited.
        using var free = JsonDocument.Parse(await (await host.SendAsync(colleague, HttpMethod.Get, "/api/task-management/assignment-limit")).Content.ReadAsStringAsync());
        Assert.False(free.RootElement.GetProperty("limited").GetBoolean());
        Assert.Equal(JsonValueKind.Null, free.RootElement.GetProperty("allowedUserIds").ValueKind);
    }

    [Fact]
    public async Task ManageAll_SeesOnlyTheTasksOfItsOwnPeople()
    {
        var mine = await host.UserAsync(host.TenantA, Permissions);
        var theirs = await host.UserAsync(host.TenantA, Permissions);
        var manager = await host.UserAsync(host.TenantA, [.. Permissions, TaskManagementPermissions.ManageAll]);
        var ours = await host.CreatedTaskIdAsync(mine, "Ours");
        var other = await host.CreatedTaskIdAsync(theirs, "Theirs");

        Assert.Contains(other, await host.ListTaskIdsAsync(manager));
        host.Reach.Keep(manager.Id, [manager.Id, mine.Id], elsewhere: [theirs.Id]);

        var seen = await host.ListTaskIdsAsync(manager);
        Assert.Contains(ours, seen);
        Assert.DoesNotContain(other, seen);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(manager, HttpMethod.Get, $"/api/task-management/tasks/{other}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(manager, HttpMethod.Get, $"/api/task-management/tasks/{ours}")).StatusCode);
    }

    [Fact]
    public async Task OtherCompaniesPeople_AreNotListed_NorMessaged_ButAnyoneOfOnesOwnIs()
    {
        var me = await host.UserAsync(host.TenantA, [IdentityPermissions.UsersView]);
        var colleague = await host.UserAsync(host.TenantA, []);
        var outsider = await host.UserAsync(host.TenantA, []);
        var unplaced = await host.UserAsync(host.TenantA, []);
        host.Reach.Keep(me.Id, [me.Id, colleague.Id], elsewhere: [outsider.Id]);

        // The user list leaves out other companies' people - not those with no place yet (to place them).
        var listed = await UserIdsAsync(me);
        Assert.Contains(colleague.Id, listed);
        Assert.Contains(unplaced.Id, listed);
        Assert.DoesNotContain(outsider.Id, listed);

        // Chat lists and reaches only one's own people.
        using var directory = JsonDocument.Parse(await (await host.SendAsync(me, HttpMethod.Get, "/api/chat/direct/directory")).Content.ReadAsStringAsync());
        var contacts = directory.RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(colleague.Id, contacts);
        Assert.DoesNotContain(outsider.Id, contacts);
        Assert.DoesNotContain(unplaced.Id, contacts);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendFormAsync(me, HttpMethod.Post, $"/api/chat/direct/{outsider.Id}/messages", "سلام")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendFormAsync(me, HttpMethod.Post, $"/api/chat/direct/{colleague.Id}/messages", "سلام")).StatusCode);
    }

    private async Task<List<Guid>> UserIdsAsync(AccessControlTests.Caller caller)
    {
        var response = await host.SendAsync(caller, HttpMethod.Get, "/api/identity/users?pageNumber=1&pageSize=500");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
    }

    private Task<HttpResponseMessage> CreateAsync(AccessControlTests.Caller owner, Guid[] responsible, Guid[]? access = null) =>
        host.SendAsync(owner, HttpMethod.Post, "/api/task-management/tasks", new
        {
            title = "Report", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = responsible, assigneeUserIds = access,
        });
}

/// <summary>A stand-in for a product's company limit: nobody is kept until a test keeps a user to a set.</summary>
public sealed class ProgrammableReachScope : IUserReachScope
{
    private readonly ConcurrentDictionary<Guid, UserReach> _kept = new();

    public void Keep(Guid userId, Guid[] reach, Guid[] elsewhere) => _kept[userId] = new UserReach(reach.ToHashSet(), elsewhere.ToHashSet());

    public Task<UserReach?> GetReachAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_kept.TryGetValue(userId, out var reach) ? reach : null);
}
