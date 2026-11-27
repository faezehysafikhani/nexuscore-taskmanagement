using System.Net;
using System.Text.Json;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>Quick add and bulk actions (phase 3) through the real endpoints.</summary>
public sealed class TaskBatchTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Tasks = "/api/task-management/tasks";

    private static readonly string[] Permissions =
        [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> QuickAsync(AccessControlTests.Caller user, string title)
    {
        var response = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/quick", new { title });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task QuickAdd_NeedsOnlyATitle_AndGivesItToTheCaller()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var response = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/quick", new { title = "  Call the supplier " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("Call the supplier", body.GetProperty("title").GetString());
        Assert.Equal(user.Id, body.GetProperty("assignedUser").GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/quick", new { title = " " })).StatusCode);
    }

    [Fact]
    public async Task Bulk_AppliesToEveryTask_AndReportsEachOnItsOwn()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var a = await QuickAsync(user, "Bulk A");
        var b = await QuickAsync(user, "Bulk B");
        var stranger = Guid.NewGuid();

        var response = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/bulk", new { ids = new[] { a, b, stranger }, action = "Priority", priority = "High" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Json(response);
        Assert.Equal(2, result.GetProperty("succeeded").GetArrayLength());
        var failed = result.GetProperty("failed");
        Assert.Equal(1, failed.GetArrayLength());
        Assert.Equal(stranger, failed[0].GetProperty("id").GetGuid());
        Assert.Equal("not_found", failed[0].GetProperty("code").GetString());

        Assert.Equal("High", (await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{a}"))).GetProperty("priority").GetString());
    }

    [Fact]
    public async Task Bulk_ArchiveAndRestore_AndStatus()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var a = await QuickAsync(user, "Bulk C");
        var b = await QuickAsync(user, "Bulk D");
        var ids = new[] { a, b };

        Assert.Equal(2, (await Json(await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/bulk", new { ids, action = "Status", status = "Completed" })))
            .GetProperty("succeeded").GetArrayLength());
        Assert.Equal("Completed", (await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{a}"))).GetProperty("status").GetString());

        Assert.Equal(2, (await Json(await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/bulk", new { ids, action = "Archive" })))
            .GetProperty("succeeded").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{a}")).StatusCode);

        Assert.Equal(2, (await Json(await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/bulk", new { ids, action = "Restore" })))
            .GetProperty("succeeded").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{a}")).StatusCode);
    }

    [Fact]
    public async Task Bulk_RefusesBadInput_AndNeedsEditPermission()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var a = await QuickAsync(user, "Bulk E");

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/bulk", new { ids = Array.Empty<Guid>(), action = "Archive" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/bulk", new { ids = new[] { a }, action = "Priority" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/bulk", new { ids = Enumerable.Repeat(0, 101).Select(_ => Guid.NewGuid()), action = "Archive" })).StatusCode);

        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(viewer, HttpMethod.Post, $"{Tasks}/bulk", new { ids = new[] { a }, action = "Archive" })).StatusCode);
    }
}
