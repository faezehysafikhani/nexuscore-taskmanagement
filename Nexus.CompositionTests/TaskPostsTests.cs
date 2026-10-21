using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NexusCore.Application.Identity.Interfaces;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>
/// Where tasks are given to posts (ITaskPosts on), a task is given to posts and their holders are
/// its people; when a post's holder changes, the task moves to the new holder.
/// </summary>
public sealed class TaskPostsTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] Permissions =
        [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.Assign, TaskManagementPermissions.Comment];

    [Fact]
    public async Task ATask_IsGivenToPosts_AndTheirHoldersAreItsPeople()
    {
        host.TaskPosts.Enabled = true;
        var (creator, expert, clerk, nobody) = (await UserAsync(), await UserAsync(), await UserAsync(), await UserAsync());
        var creatorPost = host.TaskPosts.Post("مدیر", creator.Id);
        var expertPost = host.TaskPosts.Post("کارشناس", expert.Id);
        var clerkPost = host.TaskPosts.Post("کارمند", clerk.Id);
        var vacant = host.TaskPosts.Post("کارآموز", null);

        var created = await CreateAsync(creator, new { responsiblePostIds = new[] { expertPost }, accessPostIds = new[] { clerkPost } });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var task = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(expert.Id, Assert.Single(task.RootElement.GetProperty("responsibleUsers").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Contains(task.RootElement.GetProperty("assignees").EnumerateArray(), a => a.GetProperty("id").GetGuid() == clerk.Id);
        var posts = task.RootElement.GetProperty("posts").EnumerateArray().Select(p => (p.GetProperty("postId").GetGuid(), p.GetProperty("role").GetString())).ToList();
        Assert.Equal([(creatorPost, "owner"), (expertPost, "responsible"), (clerkPost, "access")], posts);

        // A vacant post can't be given a task, nor can someone with no post (sent as a person).
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(creator, new { responsiblePostIds = new[] { vacant } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(creator, new { responsibleUserIds = new[] { nobody.Id } })).StatusCode);

        // People sent instead of posts (older clients) go by their posts.
        using var byPerson = JsonDocument.Parse(await (await CreateAsync(creator, new { responsibleUserIds = new[] { clerk.Id } })).Content.ReadAsStringAsync());
        Assert.Contains(byPerson.RootElement.GetProperty("posts").EnumerateArray(),
            p => p.GetProperty("postId").GetGuid() == clerkPost && p.GetProperty("role").GetString() == "responsible");
    }

    [Fact]
    public async Task WhenAPostChangesHands_ItsTasksMoveToTheNewHolder()
    {
        host.TaskPosts.Enabled = true;
        var (creator, before, after) = (await UserAsync(), await UserAsync(), await UserAsync());
        host.TaskPosts.Post("مدیر", creator.Id);
        var post = host.TaskPosts.Post("کارشناس", before.Id);
        using var created = JsonDocument.Parse(await (await CreateAsync(creator, new { responsiblePostIds = new[] { post } })).Content.ReadAsStringAsync());
        var taskId = created.RootElement.GetProperty("id").GetGuid();
        Assert.True((await host.SendAsync(before, HttpMethod.Get, $"/api/task-management/tasks/{taskId}")).IsSuccessStatusCode);

        host.TaskPosts.Hold(post, after.Id);
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITaskPeopleUpdater>().ApplyAsync(
                host.TenantA, [new TaskPersonChange(taskId, TaskPostRole.Responsible, before.Id, after.Id)], default);
        }

        using var moved = JsonDocument.Parse(await (await host.SendAsync(after, HttpMethod.Get, $"/api/task-management/tasks/{taskId}")).Content.ReadAsStringAsync());
        Assert.Equal(after.Id, Assert.Single(moved.RootElement.GetProperty("responsibleUsers").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.False((await host.SendAsync(before, HttpMethod.Get, $"/api/task-management/tasks/{taskId}")).IsSuccessStatusCode);

        // The task screen learns it chooses posts.
        using var choices = JsonDocument.Parse(await (await host.SendAsync(creator, HttpMethod.Get, "/api/task-management/assignable-posts")).Content.ReadAsStringAsync());
        Assert.True(choices.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Contains(choices.RootElement.GetProperty("posts").EnumerateArray(), p => p.GetProperty("postId").GetGuid() == post);
    }

    [Fact]
    public async Task AStandIn_SeesTheTaskAndComments_AsAStandIn_ButChangesNothing()
    {
        host.TaskPosts.Enabled = true;
        var (creator, worker, standIn, stranger) = (await UserAsync(), await UserAsync(), await UserAsync(), await UserAsync());
        host.TaskPosts.Post("مدیر", creator.Id);
        var post = host.TaskPosts.Post("کارشناس", worker.Id);
        using var created = JsonDocument.Parse(await (await CreateAsync(creator, new { responsiblePostIds = new[] { post } })).Content.ReadAsStringAsync());
        var taskId = created.RootElement.GetProperty("id").GetGuid();

        // The worker leaves (the post is vacant) and a stand-in is put in charge of it.
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITaskPeopleUpdater>().ApplyAsync(host.TenantA,
            [
                new TaskPersonChange(taskId, TaskPostRole.Responsible, worker.Id, null),
                new TaskPersonChange(taskId, TaskPostRole.Acting, null, standIn.Id, "جانشین کارشناس · کد ۲"),
            ], default);
        }

        var seen = await host.SendAsync(standIn, HttpMethod.Get, $"/api/task-management/tasks/{taskId}");
        Assert.True(seen.IsSuccessStatusCode, $"{seen.StatusCode} {await seen.Content.ReadAsStringAsync()}");
        var comment = await host.SendAsync(standIn, HttpMethod.Post, $"/api/task-management/tasks/{taskId}/comments", new { text = "انجام شد" });
        Assert.True(comment.IsSuccessStatusCode);
        using (var json = JsonDocument.Parse(await comment.Content.ReadAsStringAsync()))
        {
            Assert.Equal("جانشین کارشناس · کد ۲", json.RootElement.GetProperty("actingNote").GetString());
        }

        Assert.False((await host.SendAsync(standIn, HttpMethod.Patch, $"/api/task-management/tasks/{taskId}/status", new { status = "Completed" })).IsSuccessStatusCode);
        Assert.False((await host.SendAsync(standIn, HttpMethod.Put, $"/api/task-management/tasks/{taskId}", new
        {
            title = "تغییر", dueDate = "2030-01-01", priority = "High",
        })).IsSuccessStatusCode);
        // Not part of the access list the task's screen edits.
        using var task = JsonDocument.Parse(await (await host.SendAsync(creator, HttpMethod.Get, $"/api/task-management/tasks/{taskId}")).Content.ReadAsStringAsync());
        Assert.DoesNotContain(task.RootElement.GetProperty("assignees").EnumerateArray(), a => a.GetProperty("id").GetGuid() == standIn.Id);
        // Shown as the stand-in, with the post they stand in for.
        var shown = Assert.Single(task.RootElement.GetProperty("standIns").EnumerateArray());
        Assert.Equal((standIn.Id, "جانشین کارشناس · کد ۲"), (shown.GetProperty("id").GetGuid(), shown.GetProperty("note").GetString()));

        // The creator's post falls vacant too: the task has no owner now, and is still nobody else's to see.
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITaskPeopleUpdater>().ApplyAsync(host.TenantA,
                [new TaskPersonChange(taskId, TaskPostRole.Owner, creator.Id, null)], default);
        }

        Assert.False((await host.SendAsync(stranger, HttpMethod.Get, $"/api/task-management/tasks/{taskId}")).IsSuccessStatusCode);
        Assert.False((await host.SendAsync(creator, HttpMethod.Get, $"/api/task-management/tasks/{taskId}")).IsSuccessStatusCode);
        Assert.True((await host.SendAsync(standIn, HttpMethod.Get, $"/api/task-management/tasks/{taskId}")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task PeopleChanges_ReachTasks_TheSignedInUserIsNotPartOf()
    {
        host.TaskPosts.Enabled = true;
        var (creator, worker, standIn, manager) = (await UserAsync(), await UserAsync(), await UserAsync(), await UserAsync());
        host.TaskPosts.Post("مدیر", creator.Id);
        var post = host.TaskPosts.Post("کارشناس", worker.Id);
        using var created = JsonDocument.Parse(await (await CreateAsync(creator, new { responsiblePostIds = new[] { post } })).Content.ReadAsStringAsync());
        var taskId = created.RootElement.GetProperty("id").GetGuid();

        // A manager above the vacant post (not on the task, no Tasks.ManageAll) puts a stand-in in charge.
        using (var scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Sub, manager.Id.ToString()),
                    new Claim("tenant_id", host.TenantA.ToString()),
                    new Claim("permission", "Tasks.View"),
                ], "test")),
            };
            await scope.ServiceProvider.GetRequiredService<ITaskPeopleUpdater>().ApplyAsync(host.TenantA,
            [
                new TaskPersonChange(taskId, TaskPostRole.Responsible, worker.Id, null),
                new TaskPersonChange(taskId, TaskPostRole.Acting, null, standIn.Id, "جانشین کارشناس · کد ۲"),
            ], default);
        }

        Assert.True((await host.SendAsync(standIn, HttpMethod.Get, $"/api/task-management/tasks/{taskId}")).IsSuccessStatusCode);
        Assert.False((await host.SendAsync(worker, HttpMethod.Get, $"/api/task-management/tasks/{taskId}")).IsSuccessStatusCode);
    }

    private Task<AccessControlTests.Caller> UserAsync() => host.UserAsync(host.TenantA, Permissions);

    private Task<HttpResponseMessage> CreateAsync(AccessControlTests.Caller creator, object people)
    {
        var body = JsonSerializer.SerializeToElement(people);
        var fields = body.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value);
        fields["title"] = "کار پست";
        fields["dueDate"] = "2030-01-01";
        fields["priority"] = "Medium";
        return host.SendAsync(creator, HttpMethod.Post, "/api/task-management/tasks", fields);
    }
}

/// <summary>Posts kept in memory: whoever a test puts in them.</summary>
public sealed class ProgrammableTaskPosts : ITaskPosts
{
    private readonly ConcurrentDictionary<Guid, WorkPost> _posts = new();
    private readonly ConcurrentDictionary<Guid, TaskPostSet> _tasks = new();

    public bool Enabled { get; set; }

    public Guid Post(string title, Guid? holder)
    {
        var id = Guid.NewGuid();
        _posts[id] = new WorkPost(id, holder, title, (_posts.Count + 1).ToString(), "معاونت");
        return id;
    }

    public void Hold(Guid postId, Guid? holder) => _posts[postId] = _posts[postId] with { HolderUserId = holder };

    public Task<IReadOnlyDictionary<Guid, WorkPost>> GetPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> postIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, WorkPost>>(postIds.Where(_posts.ContainsKey).Distinct().ToDictionary(id => id, id => _posts[id]));

    public Task<IReadOnlyList<WorkPost>> ListPostsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkPost>>(_posts.Values.ToList());

    public Task<IReadOnlyDictionary<Guid, Guid>> GetMainPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(userIds.Distinct()
            .Select(user => (user, post: _posts.Values.FirstOrDefault(p => p.HolderUserId == user)))
            .Where(x => x.post is not null)
            .ToDictionary(x => x.user, x => x.post!.PostId));

    public Task<IReadOnlyDictionary<Guid, TaskPostSet>> GetTaskPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, TaskPostSet>>(taskIds.Where(_tasks.ContainsKey).ToDictionary(id => id, id => _tasks[id]));

    public Task SetTaskPostsAsync(
        Guid tenantId, Guid taskId, Guid? ownerPostId, IReadOnlyList<Guid>? responsiblePostIds, IReadOnlyList<Guid>? accessPostIds,
        CancellationToken cancellationToken)
    {
        var current = _tasks.GetValueOrDefault(taskId) ?? new TaskPostSet(null, [], []);
        _tasks[taskId] = new TaskPostSet(
            current.OwnerPostId ?? ownerPostId,
            responsiblePostIds?.ToList() ?? current.ResponsiblePostIds,
            accessPostIds?.ToList() ?? current.AccessPostIds);
        return Task.CompletedTask;
    }
}
