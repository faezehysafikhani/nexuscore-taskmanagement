using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;
using Nexus.TaskManagement.Permissions;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.CompositionTests;

/// <summary>
/// A new task always has someone responsible for doing it: the creator themselves or another
/// active user of the same organization - checked by the real HTTP endpoints. Older tasks
/// without one stay editable.
/// </summary>
public sealed class TaskResponsibleTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] Permissions =
    [
        TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit,
        TaskManagementPermissions.ManageRecurring
    ];

    [Fact]
    public async Task ATaskWithoutSomeoneResponsible_IsRefused()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);

        var none = await CreateAsync(user, new { title = "Nobody's", dueDate = "2030-01-01", priority = "Medium" });
        var empty = await CreateAsync(user, new { title = "Nobody's", dueDate = "2030-01-01", priority = "Medium", assignedUserId = Guid.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("responsible", await none.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheCreator_OrAnotherActiveUserOfTheOrganization_CanBeResponsible()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var colleague = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);

        var self = await CreateAsync(user, new { title = "Mine", dueDate = "2030-01-01", priority = "Medium", assignedUserId = user.Id });
        var other = await CreateAsync(user, new { title = "Theirs", dueDate = "2030-01-01", priority = "Medium", assignedUserId = colleague.Id });

        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        Assert.Equal(user.Id, (await JsonAsync(self)).GetProperty("assignedUser").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(colleague.Id, (await JsonAsync(other)).GetProperty("assignedUser").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task AUserOfAnotherOrganization_OrADisabledUser_CannotBeResponsible()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var stranger = await host.UserAsync(host.TenantB, [TaskManagementPermissions.View]);
        var disabled = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        using (var scope = host.Services.CreateScope())
        {
            // Both stores, as the one shared SQL Server table would be.
            var core = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            (await core.Users.SingleAsync(u => u.Id == disabled.Id)).SetActive(false);
            await core.SaveChangesAsync();
            var tasks = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            (await tasks.Users.SingleAsync(u => u.Id == disabled.Id)).SetActive(false);
            await tasks.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(user, new { title = "X", dueDate = "2030-01-01", priority = "Medium", assignedUserId = stranger.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(user, new { title = "X", dueDate = "2030-01-01", priority = "Medium", assignedUserId = disabled.Id })).StatusCode);
    }

    [Fact]
    public async Task ARecurringTask_IsSaved_WithItsScheduleStartAsItsDate()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);

        // What the form sends for a recurring task: the schedule's first day and time as the date.
        var created = await CreateAsync(user, new { title = "Daily stand-up", dueDate = "2030-02-01", dueTime = "09:00:00", priority = "Medium", assignedUserId = user.Id });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var taskId = (await JsonAsync(created)).GetProperty("id").GetGuid();

        var schedule = await host.SendAsync(user, HttpMethod.Post, "/api/task-management/repetitive-tasks", new
        {
            taskId, recurrence = new { frequency = "Daily", startDate = "2030-02-01", startTime = "09:00:00" },
        });
        Assert.Equal(HttpStatusCode.OK, schedule.StatusCode);

        var read = await JsonAsync(await host.SendAsync(user, HttpMethod.Get, $"/api/task-management/tasks/{taskId}"));
        Assert.True(read.GetProperty("isRecurring").GetBoolean());
        Assert.Equal("2030-02-01", read.GetProperty("recurrence").GetProperty("startDate").GetString());
        Assert.Equal("2030-02-01", read.GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task AnOlderTaskWithoutSomeoneResponsible_StaysEditable()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var taskId = Guid.NewGuid();
        using (var scope = host.Services.CreateScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            tasks.Tasks.Add(new TaskItem(taskId, host.TenantA, "From before", new DateOnly(2029, 1, 1), TaskPriority.Medium, false, user.Id));
            await tasks.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await host.UpdateTaskAsync(user, taskId, "Renamed")).StatusCode);
    }

    private Task<HttpResponseMessage> CreateAsync(AccessControlTests.Caller caller, object body) =>
        host.SendAsync(caller, HttpMethod.Post, "/api/task-management/tasks", body);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
