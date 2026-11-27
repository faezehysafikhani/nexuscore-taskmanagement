using System.Net;
using System.Text.Json;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>
/// The archive (CAT-210) and the edit-conflict answer (CAT-209) through the real HTTP endpoints. A real row version
/// needs SQL Server (see TaskManagement.Tests); here a stale version sent by the client is what is refused.
/// </summary>
public sealed class TaskArchiveTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Tasks = "/api/task-management/tasks";

    private static readonly string[] Permissions =
        [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.Delete];

    private async Task<(AccessControlTests.Caller User, Guid Id)> NewTaskAsync(string title, bool project = false)
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var created = await host.SendAsync(user, HttpMethod.Post, Tasks, project
            ? new { title, dueDate = "2030-05-10", priority = "Medium", isProject = true, assignedUserId = user.Id, subTasks = new[] { new { title = "Part", importance = "Medium", assigneeUserIds = new[] { user.Id } } } }
            : (object)new { title, dueDate = "2030-05-10", priority = "Medium", assignedUserId = user.Id });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (user, (await Json(created)).GetProperty("id").GetGuid());
    }

    private async Task<List<Guid>> IdsAsync(AccessControlTests.Caller user, string query = "") =>
        (await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}?pageSize=200{query}")))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task AnArchivedTask_IsHiddenEverywhere_ButKept_AndComesBackAsItWas()
    {
        var (user, id) = await NewTaskAsync("Quarterly plan");
        Assert.Contains(id, await IdsAsync(user));

        var archived = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/archive");
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.NotEqual(JsonValueKind.Null, (await Json(archived)).GetProperty("archivedAtUtc").ValueKind);

        // Gone from the board, from the single read and from changes - but listed in the archive.
        Assert.DoesNotContain(id, await IdsAsync(user));
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(user, HttpMethod.Patch, $"{Tasks}/{id}/status", new { status = "InProgress" })).StatusCode);
        Assert.Contains(id, await IdsAsync(user, "&archived=true"));

        var restored = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/restore");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var body = await Json(restored);
        Assert.Equal("Quarterly plan", body.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("archivedAtUtc").ValueKind);
        Assert.Contains(id, await IdsAsync(user));
        Assert.DoesNotContain(id, await IdsAsync(user, "&archived=true"));
    }

    [Fact]
    public async Task AProject_CanBeArchived_WithItsSubtasks_AndRestored()
    {
        var (user, id) = await NewTaskAsync("Big project", project: true);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/restore")).StatusCode);

        var read = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{id}"));
        Assert.True(read.GetProperty("isProject").GetBoolean());
        Assert.Single(read.GetProperty("subTasks").EnumerateArray());
    }

    [Fact]
    public async Task AnArchivedTask_CanBeDeletedForGood_FromTheArchive()
    {
        var (user, id) = await NewTaskAsync("Mistaken");
        await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/archive");

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(user, HttpMethod.Delete, $"{Tasks}/{id}")).StatusCode);

        Assert.DoesNotContain(id, await IdsAsync(user, "&archived=true"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/restore")).StatusCode);
    }

    [Fact]
    public async Task Archiving_NeedsThePermissionToEdit_AndAnArchivedTaskStaysPrivateToThoseWhoCouldSeeIt()
    {
        var (owner, id) = await NewTaskAsync("Mine");
        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);

        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(viewer, HttpMethod.Post, $"{Tasks}/{id}/archive")).StatusCode);
        await host.SendAsync(owner, HttpMethod.Post, $"{Tasks}/{id}/archive");

        // A stranger who may edit tasks still sees neither the task nor its archive entry.
        var stranger = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View, TaskManagementPermissions.Edit]);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(stranger, HttpMethod.Post, $"{Tasks}/{id}/restore")).StatusCode);
        Assert.DoesNotContain(id, await IdsAsync(stranger, "&archived=true"));
    }

    [Fact]
    public async Task AnEditMadeOverAnOlderVersion_IsRefused_WithConflict()
    {
        var (user, id) = await NewTaskAsync("Shared plan");

        var stale = await host.SendAsync(user, HttpMethod.Put, $"{Tasks}/{id}", new
        {
            title = "Shared plan v2", dueDate = "2030-05-10", priority = "Medium", assignedUserId = user.Id, rowVersion = "AAAAAAAAB9E=",
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Shared plan", (await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{id}"))).GetProperty("title").GetString());

        // Without a version the edit goes through, as it always did (older clients keep working).
        var open = await host.SendAsync(user, HttpMethod.Put, $"{Tasks}/{id}", new
        {
            title = "Shared plan v2", dueDate = "2030-05-10", priority = "Medium", assignedUserId = user.Id,
        });
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
