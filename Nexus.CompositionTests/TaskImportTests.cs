using System.Net;
using System.Text.Json;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>Import with a preview (CAT-265): a CSV of tasks, checked row by row before anything is made.</summary>
public sealed class TaskImportTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Url = "/api/task-management/tasks/import";
    private static readonly string[] Permissions = [TaskManagementPermissions.View, TaskManagementPermissions.Create];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task APreview_ReportsEveryRow_AndCreatesNothing()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var csv = "title,dueDate,priority,assignee,tags\n"
            + "Good one,2030-03-01,High,,a;b\n"
            + ",2030-03-01,,,\n"
            + "Bad date,01/03/2030,,,\n"
            + "Bad priority,2030-03-01,urgent,,\n"
            + "Nobody,2030-03-01,,no-such-user,\n"
            + "\"Quoted, with comma\",۲۰۳۰-۰۳-۰۲,زیاد,,\n";
        var preview = await Json(await host.SendAsync(user, HttpMethod.Post, $"{Url}/preview", new { csv }));
        Assert.Equal(6, preview.GetProperty("total").GetInt32());
        Assert.Equal(2, preview.GetProperty("valid").GetInt32());
        Assert.Equal(4, preview.GetProperty("invalid").GetInt32());
        var rows = preview.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal("Quoted, with comma", rows[5].GetProperty("title").GetString());
        Assert.Equal("2030-03-02", rows[5].GetProperty("dueDate").GetString());
        Assert.Equal("High", rows[5].GetProperty("priority").GetString());
        Assert.Contains("title is empty", rows[1].GetProperty("errors").ToString());
        Assert.Contains("not a user of this organization", rows[4].GetProperty("errors").ToString());

        var list = await Json(await host.SendAsync(user, HttpMethod.Get, "/api/task-management/tasks?search=Good%20one"));
        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task TheImport_RefusesBadRows_UnlessToldToSkipThem_AndMakesTheGoodOnesThroughTheTaskRules()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var csv = "title;dueDate;priority;tags\nImported A;2030-04-01;Low;\"x;y\"\nBroken;nope;;\nImported B;2030-04-02;;\n";

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, Url, new { csv })).StatusCode);
        Assert.Equal(0, (await Json(await host.SendAsync(user, HttpMethod.Get, "/api/task-management/tasks?search=Imported"))).GetProperty("items").GetArrayLength());

        var done = await Json(await host.SendAsync(user, HttpMethod.Post, Url, new { csv, skipInvalid = true }));
        Assert.True(done.GetProperty("created").GetInt32() == 2, done.ToString());
        Assert.Equal(1, done.GetProperty("skipped").GetInt32());
        Assert.Equal(3, done.GetProperty("failed")[0].GetProperty("line").GetInt32());

        var items = (await Json(await host.SendAsync(user, HttpMethod.Get, "/api/task-management/tasks?search=Imported"))).GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        // They are the importer's: given to them, with the tags.
        var one = await Json(await host.SendAsync(user, HttpMethod.Get, $"/api/task-management/tasks/{items.EnumerateArray().First(i => i.GetProperty("title").GetString() == "Imported A").GetProperty("id").GetGuid()}"));
        Assert.Equal(user.Id, one.GetProperty("assignedUser").GetProperty("id").GetGuid());
        Assert.Equal(2, one.GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task AssigneesAreFoundBySignInName_AndTheHeaderAndSizeAreChecked_AndItNeedsThePermission()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var colleague = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        string username;
        using (var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(host.Services))
        {
            var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<NexusCore.Infrastructure.Persistence.NexusCoreDbContext>(scope.ServiceProvider);
            username = db.Users.Find(colleague.Id)!.Username!;
        }

        var done = await Json(await host.SendAsync(user, HttpMethod.Post, Url, new { csv = $"title,dueDate,assignee\nFor a colleague,2030-05-01,{username}\n" }));
        Assert.True(done.TryGetProperty("created", out var c) && c.GetInt32() == 1, done.ToString());
        var id = done.GetProperty("taskIds")[0].GetGuid();
        Assert.Equal(colleague.Id, (await Json(await host.SendAsync(colleague, HttpMethod.Get, $"/api/task-management/tasks/{id}"))).GetProperty("assignedUser").GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Url}/preview", new { csv = "name,when\nx,y\n" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Url}/preview", new { csv = "title,dueDate\n" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Url}/preview",
            new { csv = "title,dueDate\n" + string.Concat(Enumerable.Repeat("x,2030-01-01\n", 501)) })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(colleague, HttpMethod.Post, $"{Url}/preview", new { csv = "title,dueDate\nx,2030-01-01\n" })).StatusCode);
    }
}
