using System.Net;
using System.Text.Json;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>Requests (categories, routing, conversion to a task that keeps its source) and "message to task" - phase 5.</summary>
public sealed class TaskRequestTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Requests = "/api/task-management/requests";
    private const string Tasks = "/api/task-management/tasks";

    private static readonly string[] Requester = [TaskManagementPermissions.View, TaskManagementPermissions.SubmitRequests];
    private static readonly string[] Handler =
    [
        TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.SubmitRequests,
        TaskManagementPermissions.HandleRequests, TaskManagementPermissions.ManageRequests
    ];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> CategoryAsync(AccessControlTests.Caller admin, Guid? route, Guid? urgent)
    {
        var response = await host.SendAsync(admin, HttpMethod.Post, $"{Requests}/categories",
            new { name = "Support " + Guid.NewGuid().ToString("N")[..6], routeToUserId = route, urgentRouteToUserId = urgent, defaultPriority = "Medium" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> SubmitAsync(AccessControlTests.Caller user, object body)
    {
        var response = await host.SendAsync(user, HttpMethod.Post, Requests, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    [Fact]
    public async Task ARequest_IsRoutedByItsCategory_AndUrgencyChangesWhoGetsIt()
    {
        var admin = await host.UserAsync(host.TenantA, Handler);
        var normal = await host.UserAsync(host.TenantA, Handler);
        var urgent = await host.UserAsync(host.TenantA, Handler);
        var requester = await host.UserAsync(host.TenantA, Requester);
        var category = await CategoryAsync(admin, normal.Id, urgent.Id);

        var plain = await SubmitAsync(requester, new { title = "Printer is broken", categoryId = category });
        Assert.Equal(normal.Id, plain.GetProperty("routedTo").GetProperty("id").GetGuid());
        Assert.Equal("New", plain.GetProperty("status").GetString());

        var hot = await SubmitAsync(requester, new { title = "Server down", categoryId = category, priority = "High" });
        Assert.Equal(urgent.Id, hot.GetProperty("routedTo").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Converting_MakesATaskThatKeepsTheRequest_AndTheRequesterCanFollowIt()
    {
        var handler = await host.UserAsync(host.TenantA, Handler);
        var requester = await host.UserAsync(host.TenantA, Requester);
        var request = await SubmitAsync(requester, new { title = "Order monitors", description = "Two 27 inch", priority = "High" });
        var id = request.GetProperty("id").GetGuid();

        var converted = await host.SendAsync(handler, HttpMethod.Post, $"{Requests}/{id}/convert", new { dueDate = "2030-06-01" });
        Assert.Equal(HttpStatusCode.OK, converted.StatusCode);
        var task = await Json(converted);
        Assert.Equal("Order monitors", task.GetProperty("title").GetString());
        Assert.Equal("Request", task.GetProperty("sourceType").GetString());
        Assert.Equal(id, task.GetProperty("sourceId").GetGuid());
        var taskId = task.GetProperty("id").GetGuid();

        // The requester sees the task and their request now points to it.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(requester, HttpMethod.Get, $"{Tasks}/{taskId}")).StatusCode);
        var mine = await Json(await host.SendAsync(requester, HttpMethod.Get, Requests));
        var item = mine.EnumerateArray().First(r => r.GetProperty("id").GetGuid() == id);
        Assert.Equal("Converted", item.GetProperty("status").GetString());
        Assert.Equal(taskId, item.GetProperty("convertedTaskId").GetGuid());

        // Decided once.
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(handler, HttpMethod.Post, $"{Requests}/{id}/convert", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(handler, HttpMethod.Post, $"{Requests}/{id}/reject", new { note = "late" })).StatusCode);
    }

    [Fact]
    public async Task Rejecting_KeepsTheNote()
    {
        var handler = await host.UserAsync(host.TenantA, Handler);
        var requester = await host.UserAsync(host.TenantA, Requester);
        var id = (await SubmitAsync(requester, new { title = "A pony" })).GetProperty("id").GetGuid();

        var rejected = await Json(await host.SendAsync(handler, HttpMethod.Post, $"{Requests}/{id}/reject", new { note = "Not in budget" }));
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("Not in budget", rejected.GetProperty("decisionNote").GetString());
    }

    [Fact]
    public async Task Visibility_AndPermissions()
    {
        var handler = await host.UserAsync(host.TenantA, Handler);
        var requester = await host.UserAsync(host.TenantA, Requester);
        var other = await host.UserAsync(host.TenantA, Requester);
        var id = (await SubmitAsync(requester, new { title = "Private ask" })).GetProperty("id").GetGuid();

        var othersView = await Json(await host.SendAsync(other, HttpMethod.Get, Requests));
        Assert.DoesNotContain(othersView.EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);

        // The inbox, deciding and categories are for handlers.
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(requester, HttpMethod.Get, $"{Requests}?mine=false")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(requester, HttpMethod.Post, $"{Requests}/{id}/convert", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(requester, HttpMethod.Post, $"{Requests}/categories", new { name = "X" })).StatusCode);
        var inbox = await Json(await host.SendAsync(handler, HttpMethod.Get, $"{Requests}?mine=false"));
        Assert.Contains(inbox.EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task AMessage_BecomesATask_WithTheMessageAsItsSource()
    {
        var user = await host.UserAsync(host.TenantA, Handler);
        var messageId = Guid.NewGuid();
        var response = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/from-message",
            new { messageId, text = "Please send the report by Sunday\nthanks" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = await Json(response);
        Assert.Equal("Please send the report by Sunday", task.GetProperty("title").GetString());
        Assert.Contains("thanks", task.GetProperty("description").GetString());
        Assert.Equal("ChatMessage", task.GetProperty("sourceType").GetString());
        Assert.Equal(messageId, task.GetProperty("sourceId").GetGuid());

        var read = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{task.GetProperty("id").GetGuid()}"));
        Assert.Equal(messageId, read.GetProperty("sourceId").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/from-message", new { messageId, text = " " })).StatusCode);
    }
}
