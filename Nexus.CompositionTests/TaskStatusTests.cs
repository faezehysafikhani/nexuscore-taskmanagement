using System.Net;
using System.Text.Json;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>Custom task statuses (phase 3): a label on top of the four built-in statuses, through the real endpoints.</summary>
public sealed class TaskStatusTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Tasks = "/api/task-management/tasks";
    private const string Statuses = "/api/task-management/statuses";

    private static readonly string[] Permissions =
    [
        TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit,
        TaskManagementPermissions.ManageStatuses
    ];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(AccessControlTests.Caller User, Guid TaskId)> NewTaskAsync()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var created = await host.SendAsync(user, HttpMethod.Post, Tasks,
            new { title = "Review me", dueDate = "2030-05-10", priority = "Medium", assignedUserId = user.Id });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (user, (await Json(created)).GetProperty("id").GetGuid());
    }

    private async Task<Guid> NewStatusAsync(AccessControlTests.Caller user, string name, string category)
    {
        var created = await host.SendAsync(user, HttpMethod.Post, Statuses, new { name, category, color = "#7c3aed", sortOrder = 1 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task ACustomStatus_ShowsOnTheTask_AndCountsAsItsCategory()
    {
        var (user, taskId) = await NewTaskAsync();
        var inReview = await NewStatusAsync(user, "In review " + Guid.NewGuid().ToString("N")[..6], "InProgress");

        var changed = await host.SendAsync(user, HttpMethod.Patch, $"{Tasks}/{taskId}/status", new { customStatusId = inReview });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var body = await Json(changed);
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
        Assert.Equal(inReview, body.GetProperty("customStatusId").GetGuid());

        // Filterable by the custom status; and a plain built-in change drops the label.
        var list = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}?customStatusId={inReview}"));
        Assert.Equal(taskId, list.GetProperty("items")[0].GetProperty("id").GetGuid());

        var plain = await Json(await host.SendAsync(user, HttpMethod.Patch, $"{Tasks}/{taskId}/status", new { status = "Completed" }));
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("customStatusId").ValueKind);
    }

    [Fact]
    public async Task ChangeStatus_NeedsExactlyOne_AndRefusesInactiveOrUnknown()
    {
        var (user, taskId) = await NewTaskAsync();
        var path = $"{Tasks}/{taskId}/status";

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Patch, path, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await host.SendAsync(user, HttpMethod.Patch, path, new { status = "Todo", customStatusId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await host.SendAsync(user, HttpMethod.Patch, path, new { customStatusId = Guid.NewGuid() })).StatusCode);

        var id = await NewStatusAsync(user, "Parked " + Guid.NewGuid().ToString("N")[..6], "Paused");
        var name = (await Json(await host.SendAsync(user, HttpMethod.Get, Statuses))).EnumerateArray()
            .First(s => s.GetProperty("id").GetGuid() == id).GetProperty("name").GetString();
        var update = await host.SendAsync(user, HttpMethod.Put, $"{Statuses}/{id}",
            new { name, category = "Paused", sortOrder = 1, isActive = false });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Patch, path, new { customStatusId = id })).StatusCode);
    }

    [Fact]
    public async Task DeletingAStatus_KeepsItsTasks_WithTheirCategory()
    {
        var (user, taskId) = await NewTaskAsync();
        var id = await NewStatusAsync(user, "Waiting " + Guid.NewGuid().ToString("N")[..6], "Paused");
        await host.SendAsync(user, HttpMethod.Patch, $"{Tasks}/{taskId}/status", new { customStatusId = id });

        Assert.True((await host.SendAsync(user, HttpMethod.Delete, $"{Statuses}/{id}")).IsSuccessStatusCode);

        var task = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{taskId}"));
        Assert.Equal("Paused", task.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("customStatusId").ValueKind);
    }

    [Fact]
    public async Task ManagingStatuses_NeedsItsPermission_AndNamesAreUnique()
    {
        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await host.SendAsync(viewer, HttpMethod.Post, Statuses, new { name = "X", category = "Todo" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(viewer, HttpMethod.Get, Statuses)).StatusCode);

        var admin = await host.UserAsync(host.TenantA, Permissions);
        var name = "Unique " + Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(admin, HttpMethod.Post, Statuses, new { name, category = "Todo" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(admin, HttpMethod.Post, Statuses, new { name, category = "Todo" })).StatusCode);
    }
}
