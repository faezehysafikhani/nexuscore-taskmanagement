using System.Net;
using System.Text.Json;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>
/// The dates and times picked in the task form (due date/time, project charter start/end) are
/// stored exactly as sent through the real HTTP endpoints and come back unchanged.
/// </summary>
public sealed class TaskDateTimeTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] Permissions =
        [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit];

    [Fact]
    public async Task DueAndCharterDatesAndTimes_AreStoredAsPicked_AndReplacedOnEdit()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);

        var created = await host.SendAsync(user, HttpMethod.Post, "/api/task-management/tasks", new
        {
            title = "Project with times", dueDate = "2030-05-10", dueTime = "14:30:00", priority = "Medium", isProject = true,
            charterStartDate = "2030-05-01", charterStartTime = "08:15:00", charterEndDate = "2030-06-01", charterEndTime = "17:45:00",
            subTasks = new[] { new { title = "Design", importance = "Medium" } },
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();

        var read = await Json(await host.SendAsync(user, HttpMethod.Get, $"/api/task-management/tasks/{id}"));
        Assert.Equal("2030-05-10", read.GetProperty("dueDate").GetString());
        Assert.Equal("14:30:00", read.GetProperty("dueTime").GetString());
        Assert.Equal("2030-05-01", read.GetProperty("charterStartDate").GetString());
        Assert.Equal("08:15:00", read.GetProperty("charterStartTime").GetString());
        Assert.Equal("2030-06-01", read.GetProperty("charterEndDate").GetString());
        Assert.Equal("17:45:00", read.GetProperty("charterEndTime").GetString());

        var edited = await host.SendAsync(user, HttpMethod.Put, $"/api/task-management/tasks/{id}", new
        {
            title = "Project with times", dueDate = "2030-07-02", dueTime = "00:00:00", priority = "Medium", isProject = true,
            charterStartDate = "2030-07-01", charterStartTime = "09:00:00", charterEndDate = "2030-07-20", charterEndTime = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        read = await Json(await host.SendAsync(user, HttpMethod.Get, $"/api/task-management/tasks/{id}"));
        Assert.Equal("2030-07-02", read.GetProperty("dueDate").GetString());
        Assert.Equal("00:00:00", read.GetProperty("dueTime").GetString());
        Assert.Equal("2030-07-01", read.GetProperty("charterStartDate").GetString());
        Assert.Equal("09:00:00", read.GetProperty("charterStartTime").GetString());
        Assert.Equal("2030-07-20", read.GetProperty("charterEndDate").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("charterEndTime").ValueKind);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
