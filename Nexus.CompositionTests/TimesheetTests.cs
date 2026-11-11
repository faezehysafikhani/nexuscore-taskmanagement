using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.CompositionTests;

/// <summary>Working hours (phase 6): entries, timer, estimate against actual, weekly approval and workload.</summary>
public sealed class TimesheetTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Sheets = "/api/task-management/timesheets";
    private const string Tasks = "/api/task-management/tasks";
    private static readonly string[] Permissions =
        [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.LogTime];
    private static readonly string Today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> QuickAsync(AccessControlTests.Caller user, string title = "Work")
    {
        var response = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/quick", new { title });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> LogAsync(AccessControlTests.Caller user, Guid taskId, int minutes, string? date = null)
    {
        var response = await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/entries", new { taskId, date = date ?? Today, minutes });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Entries_AreValidated_AndEditableUntilSent()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var task = await QuickAsync(user);

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/entries", new { taskId = task, date = Today, minutes = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/entries", new { taskId = task, date = "2099-01-01", minutes = 30 })).StatusCode);
        var id = await LogAsync(user, task, 90);
        // A day has 24 hours.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/entries", new { taskId = task, date = Today, minutes = 1400 })).StatusCode);

        var changed = await host.SendAsync(user, HttpMethod.Put, $"{Sheets}/entries/{id}", new { date = Today, minutes = 45, note = "fixed" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(45, (await Json(changed)).GetProperty("minutes").GetInt32());
        Assert.True((await host.SendAsync(user, HttpMethod.Delete, $"{Sheets}/entries/{id}")).IsSuccessStatusCode);

        // Someone else's entry is not found.
        var other = await host.UserAsync(host.TenantA, Permissions);
        var mine = await LogAsync(user, task, 10);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(other, HttpMethod.Delete, $"{Sheets}/entries/{mine}")).StatusCode);
    }

    [Fact]
    public async Task EstimateAgainstActual_OnTheTask()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var task = await QuickAsync(user);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Put, $"{Tasks}/{task}/estimate", new { minutes = 0 })).StatusCode);
        var estimated = await Json(await host.SendAsync(user, HttpMethod.Put, $"{Tasks}/{task}/estimate", new { minutes = 240 }));
        Assert.Equal(240, estimated.GetProperty("estimatedMinutes").GetInt32());

        await LogAsync(user, task, 60);
        await LogAsync(user, task, 30);
        var time = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{task}/time"));
        Assert.Equal(90, time.GetProperty("actualMinutes").GetInt32());
        Assert.Equal(2, time.GetProperty("entries").GetArrayLength());
        Assert.Equal(240, (await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{task}"))).GetProperty("estimatedMinutes").GetInt32());
    }

    [Fact]
    public async Task TheTimer_MakesAnEntry_AndStartingAnotherStopsTheFirst()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var first = await QuickAsync(user, "First");
        var second = await QuickAsync(user, "Second");

        var none = await host.SendAsync(user, HttpMethod.Get, $"{Sheets}/timer");
        Assert.Equal(HttpStatusCode.OK, none.StatusCode);
        Assert.Equal(string.Empty, await none.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/timer/start", new { taskId = first })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/timer/start", new { taskId = first })).StatusCode);

        // Switching: the first is logged (at least a minute), the second runs.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/timer/start", new { taskId = second })).StatusCode);
        Assert.Equal(second, (await Json(await host.SendAsync(user, HttpMethod.Get, $"{Sheets}/timer"))).GetProperty("taskId").GetGuid());
        Assert.Equal(1, (await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{first}/time"))).GetProperty("actualMinutes").GetInt32());

        var stopped = await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/timer/stop");
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        Assert.Equal("Timer", (await Json(stopped)).GetProperty("source").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/timer/stop")).StatusCode);
    }

    [Fact]
    public async Task SendingAWeek_WithNoApprovalChain_ApprovesItAtOnce_AndLocksTheEntries()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var task = await QuickAsync(user);
        var entry = await LogAsync(user, task, 120);

        var week = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Sheets}/week?day={Today}"));
        Assert.True(week.GetProperty("canSubmit").GetBoolean());
        Assert.Equal(120, week.GetProperty("totalMinutes").GetInt32());

        var sent = await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/submit", new { weekStart = Today });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var body = await Json(sent);
        Assert.Equal("Approved", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("canSubmit").GetBoolean());

        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(user, HttpMethod.Put, $"{Sheets}/entries/{entry}", new { date = Today, minutes = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(user, HttpMethod.Delete, $"{Sheets}/entries/{entry}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/submit", new { weekStart = Today })).StatusCode);
    }

    private async Task<Guid> PutWeekInReviewAsync(Guid tenantId, Guid userId, Guid entryId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        var entry = await db.TimeEntries.FirstAsync(e => e.Id == entryId);
        var submission = new TimesheetSubmission(Guid.NewGuid(), tenantId, userId, Nexus.TaskManagement.Application.TimesheetService.WeekStartOf(DateOnly.Parse(Today)), entry.Minutes, DateTimeOffset.UtcNow);
        db.TimesheetSubmissions.Add(submission);
        entry.MarkSubmitted(submission.Id);
        await db.SaveChangesAsync();
        return submission.Id;
    }

    private async Task DecideAsync(Guid tenantId, Guid submissionId, bool approve)
    {
        using var scope = host.Services.CreateScope();
        if (approve)
            foreach (var h in scope.ServiceProvider.GetServices<IDomainEventHandler<ApprovalGranted>>())
                await h.HandleAsync(new ApprovalGranted("Timesheet", submissionId, tenantId, null, "ok"), default);
        else
            foreach (var h in scope.ServiceProvider.GetServices<IDomainEventHandler<ApprovalRejected>>())
                await h.HandleAsync(new ApprovalRejected("Timesheet", submissionId, tenantId, null, "fix the Tuesday entry"), default);
    }

    [Fact]
    public async Task ARejectedWeek_OpensUpAgain_AndAnApprovedOneIsFinal()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var task = await QuickAsync(user);
        var entry = await LogAsync(user, task, 60);
        var submission = await PutWeekInReviewAsync(host.TenantA, user.Id, entry);

        var pending = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Sheets}/week?day={Today}"));
        Assert.Equal("PendingApproval", pending.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/submit", new { weekStart = Today })).StatusCode);
        var waiting = await Json(await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/in-review", new { ids = new[] { submission } }));
        Assert.Equal(60, waiting[0].GetProperty("totalMinutes").GetInt32());

        await DecideAsync(host.TenantA, submission, approve: false);
        var rejected = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Sheets}/week?day={Today}"));
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("fix the Tuesday entry", rejected.GetProperty("decisionComment").GetString());
        Assert.True(rejected.GetProperty("canSubmit").GetBoolean());

        // Corrected and sent again.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Put, $"{Sheets}/entries/{entry}", new { date = Today, minutes = 50 })).StatusCode);
        Assert.Equal("Approved", (await Json(await host.SendAsync(user, HttpMethod.Post, $"{Sheets}/submit", new { weekStart = Today }))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Workload_IsForTimeManagers_AndShowsLoadAgainstCapacity()
    {
        var user = await host.UserAsync(host.TenantB, Permissions);
        var task = await QuickAsync(user, "Big job");
        await host.SendAsync(user, HttpMethod.Put, $"{Tasks}/{task}/estimate", new { minutes = 600 });
        await LogAsync(user, task, 120);

        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(user, HttpMethod.Get, $"{Sheets}/workload")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(user, HttpMethod.Get, $"{Sheets}/week?userId={Guid.NewGuid()}")).StatusCode);

        var manager = await host.UserAsync(host.TenantB, [.. Permissions, TaskManagementPermissions.ViewAllTime]);
        var workload = await Json(await host.SendAsync(manager, HttpMethod.Get, $"{Sheets}/workload?capacityMinutes=2400"));
        var row = workload.GetProperty("people").EnumerateArray().First(r => r.GetProperty("user").GetProperty("id").GetGuid() == user.Id);
        Assert.Equal(120, row.GetProperty("loggedMinutes").GetInt32());
        Assert.Equal(1, row.GetProperty("openTasks").GetInt32());
        Assert.Equal(480, row.GetProperty("remainingEstimatedMinutes").GetInt32());
        Assert.Equal(0.25, row.GetProperty("utilization").GetDouble());

        // A manager reads another person's week.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(manager, HttpMethod.Get, $"{Sheets}/week?userId={user.Id}")).StatusCode);
    }
}
