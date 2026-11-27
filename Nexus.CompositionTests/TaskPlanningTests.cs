using System.Net;
using System.Text.Json;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>Dependencies, blocker, acceptance criteria, WIP limit, bulk subtasks and templates (phase 3c).</summary>
public sealed class TaskPlanningTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Tasks = "/api/task-management/tasks";
    private static readonly string[] Permissions =
    [
        TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit,
        TaskManagementPermissions.ManageStatuses, TaskManagementPermissions.ManageTemplates
    ];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> QuickAsync(AccessControlTests.Caller user, string title)
    {
        var response = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/quick", new { title });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Status(AccessControlTests.Caller user, Guid id, string status) =>
        host.SendAsync(user, HttpMethod.Patch, $"{Tasks}/{id}/status", new { status });

    [Fact]
    public async Task ATask_CannotBeCompleted_WhileItsBlockerIsOpen()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var first = await QuickAsync(user, "Design");
        var second = await QuickAsync(user, "Build");

        var added = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{second}/dependencies", new { blockedByTaskId = first });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var planning = await Json(added);
        Assert.False(planning.GetProperty("canComplete").GetBoolean());
        Assert.Equal(first, planning.GetProperty("blockedBy")[0].GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.Conflict, (await Status(user, second, "Completed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Status(user, first, "Completed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Status(user, second, "Completed")).StatusCode);
    }

    [Fact]
    public async Task Dependencies_RefuseSelf_Duplicates_AndCycles()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var a = await QuickAsync(user, "A");
        var b = await QuickAsync(user, "B");
        var c = await QuickAsync(user, "C");

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{a}/dependencies", new { blockedByTaskId = a })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{b}/dependencies", new { blockedByTaskId = a })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{b}/dependencies", new { blockedByTaskId = a })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{c}/dependencies", new { blockedByTaskId = b })).StatusCode);
        // a -> b -> c: c cannot be a blocker of a.
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{a}/dependencies", new { blockedByTaskId = c })).StatusCode);

        var removed = await host.SendAsync(user, HttpMethod.Delete, $"{Tasks}/{b}/dependencies/{a}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(0, (await Json(removed)).GetProperty("blockedBy").GetArrayLength());
    }

    [Fact]
    public async Task AcceptanceCriteria_MustBeMet_BeforeCompletion()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var id = await QuickAsync(user, "Ship it");
        var added = await Json(await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/criteria", new { text = "Docs updated" }));
        var criterion = added.GetProperty("criteria")[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Conflict, (await Status(user, id, "Completed")).StatusCode);

        var met = await Json(await host.SendAsync(user, HttpMethod.Put, $"{Tasks}/{id}/criteria/{criterion}/met", new { met = true }));
        Assert.True(met.GetProperty("canComplete").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await Status(user, id, "Completed")).StatusCode);
    }

    [Fact]
    public async Task TheBlocker_IsFreeText_AndDoesNotStopCompletion()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var id = await QuickAsync(user, "Waiting");
        var set = await Json(await host.SendAsync(user, HttpMethod.Put, $"{Tasks}/{id}/blocker", new { reason = "Supplier has not answered" }));
        Assert.Equal("Supplier has not answered", set.GetProperty("blockedReason").GetString());
        Assert.Equal("Supplier has not answered", (await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{id}"))).GetProperty("blockedReason").GetString());

        var cleared = await Json(await host.SendAsync(user, HttpMethod.Put, $"{Tasks}/{id}/blocker", new { reason = " " }));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("blockedReason").ValueKind);
    }

    [Fact]
    public async Task TheWipLimit_StopsTooManyTasksInProgress()
    {
        var user = await host.UserAsync(host.TenantB, Permissions);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Put, "/api/task-management/wip", new { limitPerUser = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Put, "/api/task-management/wip", new { limitPerUser = 1 })).StatusCode);
        try
        {
            var one = await QuickAsync(user, "First");
            var two = await QuickAsync(user, "Second");
            Assert.Equal(HttpStatusCode.OK, (await Status(user, one, "InProgress")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Status(user, two, "InProgress")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Status(user, one, "Paused")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Status(user, two, "InProgress")).StatusCode);
        }
        finally
        {
            await host.SendAsync(user, HttpMethod.Put, "/api/task-management/wip", new { limitPerUser = (int?)null });
        }

        Assert.Equal(JsonValueKind.Null, (await Json(await host.SendAsync(user, HttpMethod.Get, "/api/task-management/wip"))).GetProperty("limitPerUser").ValueKind);
    }

    [Fact]
    public async Task ManySubTasks_AtOnce()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var id = await QuickAsync(user, "Parent");
        var response = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/subtasks/bulk", new { titles = new[] { "One", " ", "Two", "Three" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, (await Json(response)).GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/subtasks/bulk", new { titles = new[] { " " } })).StatusCode);
    }

    [Fact]
    public async Task ATemplate_MakesATaskWithItsSubtasksCriteriaAndTags()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var created = await host.SendAsync(user, HttpMethod.Post, "/api/task-management/templates", new
        {
            name = "Onboarding " + Guid.NewGuid().ToString("N")[..6],
            body = new
            {
                title = "Onboard a person", description = "Standard steps", priority = "High", isProject = true, dueInDays = 14,
                subTasks = new[] { "Laptop", "Accounts" }, criteria = new[] { "Manager met them" }, tags = new[] { "hr" }
            }
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var templateId = (await Json(created)).GetProperty("id").GetGuid();

        var made = await host.SendAsync(user, HttpMethod.Post, $"/api/task-management/templates/{templateId}/instantiate", new { title = "Onboard Sara" });
        Assert.Equal(HttpStatusCode.OK, made.StatusCode);
        var task = await Json(made);
        Assert.Equal("Onboard Sara", task.GetProperty("title").GetString());
        Assert.True(task.GetProperty("isProject").GetBoolean());
        Assert.Equal(2, task.GetProperty("subTasks").GetArrayLength());
        Assert.Equal("hr", task.GetProperty("tags")[0].GetProperty("name").GetString());

        var planning = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{task.GetProperty("id").GetGuid()}/planning"));
        Assert.Equal("Manager met them", planning.GetProperty("criteria")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ATask_CanBeSavedAsATemplate_AndTemplatesNeedTheirPermission()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var id = await QuickAsync(user, "Weekly report");
        var name = "Report " + Guid.NewGuid().ToString("N")[..6];
        var saved = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/save-as-template", new { name });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("Weekly report", (await Json(saved)).GetProperty("body").GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/save-as-template", new { name })).StatusCode);

        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View, TaskManagementPermissions.Create]);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(viewer, HttpMethod.Get, "/api/task-management/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(viewer, HttpMethod.Post, "/api/task-management/templates",
            new { name = "x", body = new { title = "x", priority = "Low", isProject = false, dueInDays = 1, subTasks = Array.Empty<string>(), criteria = Array.Empty<string>(), tags = Array.Empty<string>() } })).StatusCode);
    }
}
