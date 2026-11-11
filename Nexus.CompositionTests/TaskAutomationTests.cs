using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Infrastructure;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Common;

namespace Nexus.CompositionTests;

/// <summary>Automation rules, the execution log and SLA clocks (phase 7).</summary>
public sealed class TaskAutomationTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Tasks = "/api/task-management/tasks";
    private const string Rules = "/api/task-management/automation/rules";
    private static readonly string[] Permissions =
        [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.ManageAutomation];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> RuleAsync(AccessControlTests.Caller admin, string trigger, object? conditions, params object[] actions)
    {
        var response = await host.SendAsync(admin, HttpMethod.Post, Rules, new { name = "Rule " + Guid.NewGuid().ToString("N")[..6], trigger, conditions, actions });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> NewTaskAsync(AccessControlTests.Caller user, string title, string priority = "Medium")
    {
        var created = await host.SendAsync(user, HttpMethod.Post, Tasks, new { title, dueDate = "2030-05-10", priority, assignedUserId = user.Id });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return await Json(created);
    }

    [Fact]
    public async Task ARule_OnCreation_TagsTheTask_AndStartsItsSla()
    {
        var admin = await host.UserAsync(host.TenantA, Permissions);
        var marker = "VIP" + Guid.NewGuid().ToString("N")[..5];
        await RuleAsync(admin, "TaskCreated", new { priority = "High", titleContains = marker },
            new { type = "AddTag", tag = "urgent" }, new { type = "StartSla", hours = 8 });

        var hit = await NewTaskAsync(admin, $"{marker} server down", "High");
        Assert.Equal("urgent", hit.GetProperty("tags")[0].GetProperty("name").GetString());
        var due = hit.GetProperty("slaDueAtUtc").GetDateTimeOffset();
        Assert.InRange((due - DateTimeOffset.UtcNow).TotalHours, 7.9, 8.1);

        // A task that does not match is left alone.
        var miss = await NewTaskAsync(admin, $"{marker} cosmetic", "Low");
        Assert.Equal(0, miss.GetProperty("tags").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, miss.GetProperty("slaDueAtUtc").ValueKind);

        var log = await Json(await host.SendAsync(admin, HttpMethod.Get, $"/api/task-management/automation/log?taskId={hit.GetProperty("id").GetGuid()}"));
        Assert.Equal(2, log.GetArrayLength());
        Assert.All(log.EnumerateArray(), e => Assert.Equal("Applied", e.GetProperty("outcome").GetString()));
    }

    [Fact]
    public async Task ARule_OnAStatusChange_Runs_AndOneThatCannotApply_IsLoggedAsFailed()
    {
        var admin = await host.UserAsync(host.TenantA, Permissions);
        var marker = "STAT" + Guid.NewGuid().ToString("N")[..5];
        await RuleAsync(admin, "StatusChanged", new { status = "Completed", titleContains = marker }, new { type = "AddTag", tag = "shipped" });
        // No organization structure in this host: the manager cannot be found, the task keeps going.
        await RuleAsync(admin, "TaskCreated", new { titleContains = marker }, new { type = "AssignManager" });

        var task = await NewTaskAsync(admin, $"{marker} release");
        var id = task.GetProperty("id").GetGuid();
        var log = await Json(await host.SendAsync(admin, HttpMethod.Get, $"/api/task-management/automation/log?taskId={id}"));
        Assert.Equal("Failed", log[0].GetProperty("outcome").GetString());

        var done = await host.SendAsync(admin, HttpMethod.Patch, $"{Tasks}/{id}/status", new { status = "Completed" });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Equal("shipped", (await Json(done)).GetProperty("tags")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task TheSlaMonitor_MarksABreach_Once_RunsItsRules_AndThePauseStopsTheClock()
    {
        var admin = await host.UserAsync(host.TenantA, Permissions);
        var marker = "SLA" + Guid.NewGuid().ToString("N")[..5];
        await RuleAsync(admin, "TaskCreated", new { titleContains = marker }, new { type = "StartSla", hours = 1 });
        await RuleAsync(admin, "SlaBreached", new { titleContains = marker }, new { type = "AddTag", tag = "late" });

        var running = (await NewTaskAsync(admin, $"{marker} running")).GetProperty("id").GetGuid();
        var paused = (await NewTaskAsync(admin, $"{marker} paused")).GetProperty("id").GetGuid();
        var pausedTask = await Json(await host.SendAsync(admin, HttpMethod.Patch, $"{Tasks}/{paused}/status", new { status = "Paused" }));
        var dueWhenPaused = pausedTask.GetProperty("slaDueAtUtc").GetDateTimeOffset();

        var monitor = new TaskSlaMonitorService(host.Services, Options.Create(new RepetitiveTaskSchedulerOptions { Enabled = true }), TimeProvider.System, NullLogger<TaskSlaMonitorService>.Instance);
        Assert.True(await monitor.RunOnceAsync(DateTimeOffset.UtcNow.AddHours(2), default) >= 1);

        var breached = await Json(await host.SendAsync(admin, HttpMethod.Get, $"{Tasks}/{running}"));
        Assert.NotEqual(JsonValueKind.Null, breached.GetProperty("slaBreachedAtUtc").ValueKind);
        Assert.Equal("late", breached.GetProperty("tags")[0].GetProperty("name").GetString());

        // The paused one did not breach; resuming pushes the due moment on by the time it was paused.
        var stillPaused = await Json(await host.SendAsync(admin, HttpMethod.Get, $"{Tasks}/{paused}"));
        Assert.Equal(JsonValueKind.Null, stillPaused.GetProperty("slaBreachedAtUtc").ValueKind);
        await Task.Delay(1200);
        var resumed = await Json(await host.SendAsync(admin, HttpMethod.Patch, $"{Tasks}/{paused}/status", new { status = "InProgress" }));
        Assert.True(resumed.GetProperty("slaDueAtUtc").GetDateTimeOffset() > dueWhenPaused);

        // Once: a second pass leaves the one already marked alone (it only finds the task that was just resumed).
        var firstMark = breached.GetProperty("slaBreachedAtUtc").GetDateTimeOffset();
        Assert.Equal(1, await monitor.RunOnceAsync(DateTimeOffset.UtcNow.AddHours(2), default));
        Assert.Equal(firstMark, (await Json(await host.SendAsync(admin, HttpMethod.Get, $"{Tasks}/{running}"))).GetProperty("slaBreachedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public void BusinessDaySla_SkipsNonWorkingDays()
    {
        // Friday and Saturday off.
        var policy = new Closed(DayOfWeek.Friday, DayOfWeek.Saturday);
        var thursdayNoon = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero); // a Thursday
        Assert.Equal(DayOfWeek.Thursday, thursdayNoon.DayOfWeek);

        // 12h left on Thursday, so 24h of working time ends on Sunday 12:00.
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero), SlaCalculator.DueAt(thursdayNoon, 24, true, policy));
        Assert.Equal(thursdayNoon.AddHours(24), SlaCalculator.DueAt(thursdayNoon, 24, false, policy));
    }

    private sealed class Closed(params DayOfWeek[] off) : ISchedulingDayPolicy
    {
        public SchedulingDayDecision Check(DateOnly date) => off.Contains(date.DayOfWeek) ? new SchedulingDayDecision(false, "off") : SchedulingDayDecision.Allowed;
    }

    [Fact]
    public async Task Rules_NeedThePermission_AndValidInput()
    {
        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(viewer, HttpMethod.Get, Rules)).StatusCode);

        var admin = await host.UserAsync(host.TenantA, Permissions);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(admin, HttpMethod.Post, Rules, new { name = "x", trigger = "TaskCreated", actions = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(admin, HttpMethod.Post, Rules, new { name = "x", trigger = "TaskCreated", actions = new object[] { new { type = "StartSla" } } })).StatusCode);

        var id = await RuleAsync(admin, "TaskCreated", null, new { type = "AddTag", tag = "t" });
        var off = await host.SendAsync(admin, HttpMethod.Put, $"{Rules}/{id}", new { name = "Off", trigger = "TaskCreated", actions = new object[] { new { type = "AddTag", tag = "t" } }, isActive = false });
        Assert.False((await Json(off)).GetProperty("isActive").GetBoolean());
        Assert.True((await host.SendAsync(admin, HttpMethod.Delete, $"{Rules}/{id}")).IsSuccessStatusCode);
    }
}
