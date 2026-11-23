using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Permissions;
using Nexus.TaskManagement.Realtime;

namespace Nexus.CompositionTests;

/// <summary>
/// The board's batch load (POST tasks/board-details): one request for many tasks' detail, comments and history,
/// with the same access rules as the single-task endpoints, and no "TasksChanged" broadcast (it only reads).
/// </summary>
public sealed class TaskBoardDetailsTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Tasks = "/api/task-management/tasks";
    private static readonly string[] Permissions = [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.Comment];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> NewTaskAsync(AccessControlTests.Caller user, string title)
    {
        var created = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/quick", new { title });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task ReturnsDetailCommentsAndHistory_OfEachVisibleTask()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var first = await NewTaskAsync(user, "Board one");
        var second = await NewTaskAsync(user, "Board two");
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{first}/comments", new { text = "hello" })).StatusCode);

        var response = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/board-details", new { ids = new[] { first, second, Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await Json(response);

        Assert.Equal(2, items.GetArrayLength());
        var one = items.EnumerateArray().Single(i => i.GetProperty("task").GetProperty("id").GetGuid() == first);
        Assert.Equal("Board one", one.GetProperty("task").GetProperty("title").GetString());
        Assert.Equal("hello", one.GetProperty("comments")[0].GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Array, one.GetProperty("activity").ValueKind);
    }

    [Fact]
    public async Task LeavesOutTasksTheCallerCannotSee()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var id = await NewTaskAsync(owner, "Private");
        var stranger = await host.UserAsync(host.TenantA, Permissions);
        var otherTenant = await host.UserAsync(host.TenantB, Permissions);

        foreach (var caller in new[] { stranger, otherTenant })
        {
            var response = await host.SendAsync(caller, HttpMethod.Post, $"{Tasks}/board-details", new { ids = new[] { id } });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, (await Json(response)).GetArrayLength());
        }
    }

    [Fact]
    public async Task RefusesMoreThanTheLimit()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/board-details", new { ids })).StatusCode);
    }

    [Theory]
    [InlineData("/api/task-management/tasks/board-details")]
    [InlineData("/api/task-management/tasks/in-review")]
    public void ReadOnlyPosts_AreNotBroadcastAsChanges(string route)
    {
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var endpoint = endpoints.Single(e => "/" + e.RoutePattern.RawText?.TrimStart('/') == route);
        Assert.NotNull(endpoint.Metadata.GetMetadata<ReadOnlyTaskEndpoint>());
    }
}
