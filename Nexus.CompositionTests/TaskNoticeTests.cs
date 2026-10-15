using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Integrations.TaskNotifications;
using Nexus.TaskManagement.Permissions;
using Notifications.Domain.Entities;
using Notifications.Infrastructure.Persistence;

namespace Nexus.CompositionTests;

/// <summary>
/// What happens to a task reaches its people as stored, in-app notifications - the same on every
/// device - and never whoever did it, nor an administrator who only sees every task. Through the
/// real HTTP endpoints and the real Notifications module.
/// </summary>
public sealed class TaskNoticeTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] Permissions =
    [
        TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit,
        TaskManagementPermissions.Assign, TaskManagementPermissions.Delete, TaskManagementPermissions.Comment
    ];

    private static readonly string[] Worker = [TaskManagementPermissions.View, TaskManagementPermissions.Edit, TaskManagementPermissions.Comment];

    [Fact]
    public async Task AComment_ReachesTheTasksPeople_ButNotItsWriter_NorAnAdministrator()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var doer = await host.UserAsync(host.TenantA, Worker);
        var taskId = await CreateAsync(owner, [doer.Id]);

        Assert.True((await host.SendAsync(doer, HttpMethod.Post, $"/api/task-management/tasks/{taskId}/comments", new { text = "Almost done" })).IsSuccessStatusCode);

        var told = Assert.Single(await NotificationsAsync(owner.Id, TaskNoticeNotifications.Comment));
        Assert.Equal(taskId, told.ReferenceId);
        Assert.Equal(doer.Id, told.ActorUserId);
        Assert.Equal("Almost done", told.Message);
        Assert.Equal(host.TenantA, told.TenantId);
        Assert.Empty(await NotificationsAsync(doer.Id, TaskNoticeNotifications.Comment));
        Assert.Empty(await NotificationsAsync(host.Admin.Id));
    }

    [Fact]
    public async Task AnEdit_IsToldOnce_InTheTaskScreensOwnWords()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var doer = await host.UserAsync(host.TenantA, Worker);
        var taskId = await CreateAsync(owner, [doer.Id]);

        Assert.True((await host.SendAsync(owner, HttpMethod.Put, $"/api/task-management/tasks/{taskId}", new
        {
            title = "Renamed", dueDate = "2030-01-01", priority = "High", responsibleUserIds = new[] { doer.Id },
        })).IsSuccessStatusCode);
        Assert.NotEmpty(await NotificationsAsync(doer.Id, TaskNoticeNotifications.Edited));

        // The task screen then records the whole edit in its words: that one replaces the short ones.
        Assert.True((await host.SendAsync(owner, HttpMethod.Post, $"/api/task-management/tasks/{taskId}/activity", new
        {
            action = "ویرایش اطلاعات فعالیت", details = "عنوان: Renamed",
        })).IsSuccessStatusCode);

        Assert.Empty(await NotificationsAsync(doer.Id, TaskNoticeNotifications.Edited));
        var told = Assert.Single(await NotificationsAsync(doer.Id, TaskNoticeNotifications.Activity));
        Assert.Equal("ویرایش اطلاعات فعالیت", told.Title);
        Assert.Equal("عنوان: Renamed", told.Message);
        Assert.Empty(await NotificationsAsync(owner.Id));
    }

    [Fact]
    public async Task ANewTask_IsToldOnce_AndNotToItsCreator()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var doer = await host.UserAsync(host.TenantA, Worker);
        var taskId = await CreateAsync(owner, [doer.Id]);
        Assert.True((await host.SendAsync(owner, HttpMethod.Post, $"/api/task-management/tasks/{taskId}/activity", new
        {
            action = TaskNoticeNotifications.AppCreationEntry, details = "Report",
        })).IsSuccessStatusCode);

        Assert.Empty(await NotificationsAsync(doer.Id, TaskNoticeNotifications.Created));
        Assert.Single(await NotificationsAsync(doer.Id, TaskNoticeNotifications.Activity));
        Assert.Empty(await NotificationsAsync(owner.Id));
    }

    [Fact]
    public async Task RemovingSomeoneFromResponsibility_AndGivingAccess_AreTold()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var before = await host.UserAsync(host.TenantA, Worker);
        var after = await host.UserAsync(host.TenantA, Worker);
        var viewer = await host.UserAsync(host.TenantA, Worker);
        var taskId = await CreateAsync(owner, [before.Id]);

        Assert.True((await host.SendAsync(owner, HttpMethod.Patch, $"/api/task-management/tasks/{taskId}/assigned-user", new
        {
            assignedUserId = after.Id, responsibleUserIds = new[] { after.Id }, assigneeUserIds = new[] { viewer.Id },
        })).IsSuccessStatusCode);

        Assert.Single(await NotificationsAsync(before.Id, TaskNoticeNotifications.Unassigned));
        Assert.Single(await NotificationsAsync(after.Id, TaskNoticeNotifications.Assigned));
        Assert.Single(await NotificationsAsync(viewer.Id, TaskNoticeNotifications.AccessGranted));
        Assert.All(await NotificationsAsync(after.Id, TaskNoticeNotifications.Assigned), n => Assert.Equal(owner.Id, n.ActorUserId));
        Assert.DoesNotContain(await NotificationsAsync(owner.Id), n =>
            n.Category is TaskNoticeNotifications.Assigned or TaskNoticeNotifications.Unassigned or TaskNoticeNotifications.AccessGranted);
    }

    [Fact]
    public async Task ADeletedTask_IsToldToItsPeople_NotToWhoeverDeletedIt()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var doer = await host.UserAsync(host.TenantA, Worker);
        var taskId = await CreateAsync(owner, [doer.Id]);

        Assert.True((await host.SendAsync(owner, HttpMethod.Delete, $"/api/task-management/tasks/{taskId}")).IsSuccessStatusCode);

        var told = Assert.Single(await NotificationsAsync(doer.Id, TaskNoticeNotifications.Deleted));
        Assert.Contains("Report", told.Message);
        Assert.Empty(await NotificationsAsync(owner.Id, TaskNoticeNotifications.Deleted));
    }

    [Fact]
    public async Task TheCallersList_CarriesTheCategory_TheTask_AndWhoDidIt()
    {
        var owner = await host.UserAsync(host.TenantA, Permissions);
        var doer = await host.UserAsync(host.TenantA, Worker);
        var taskId = await CreateAsync(owner, [doer.Id]);
        await host.SendAsync(doer, HttpMethod.Post, $"/api/task-management/tasks/{taskId}/comments", new { text = "Hello" });

        var response = await host.SendAsync(owner, HttpMethod.Get, "/api/notifications?pageSize=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var comment = Assert.Single(json.RootElement.EnumerateArray(), item => item.GetProperty("category").GetString() == TaskNoticeNotifications.Comment);
        Assert.Equal(taskId, comment.GetProperty("referenceId").GetGuid());
        Assert.Equal(doer.Id, comment.GetProperty("actorUserId").GetGuid());
        Assert.False(string.IsNullOrEmpty(comment.GetProperty("actorName").GetString()));
    }

    private async Task<Guid> CreateAsync(AccessControlTests.Caller owner, Guid[] responsible)
    {
        var response = await host.SendAsync(owner, HttpMethod.Post, "/api/task-management/tasks", new
        {
            title = "Report", dueDate = "2030-01-01", priority = "Medium", responsibleUserIds = responsible,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<IReadOnlyList<Notification>> NotificationsAsync(Guid userId, string? category = null)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        return await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.UserId == userId && (category == null || n.Category == category))
            .ToListAsync();
    }
}
