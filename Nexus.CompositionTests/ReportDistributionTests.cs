using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;
using Nexus.TaskManagement.Permissions;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.CompositionTests;

/// <summary>Report distribution (CAT-195): a person's scheduled digest of their own tasks by SMS.</summary>
public sealed class ReportDistributionTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Url = "/api/task-management/report-subscriptions";
    private static readonly string[] Permissions = [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit];
    private static int _phone = 4000;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public void TheNextRun_IsInIranTime_ForEachFrequency()
    {
        var iran = ReportSubscription.IranOffset;
        // Wednesday 2026-11-11 10:00 in Iran.
        var now = new DateTimeOffset(2026, 11, 11, 10, 0, 0, iran);

        var daily = new ReportSubscription(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReportFrequency.Daily, null, null, 8, true, false, now);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 8, 0, 0, iran), daily.NextRunAtUtc);
        var laterToday = new ReportSubscription(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReportFrequency.Daily, null, null, 18, true, false, now);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 18, 0, 0, iran), laterToday.NextRunAtUtc);

        var weekly = new ReportSubscription(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReportFrequency.Weekly, DayOfWeek.Saturday, null, 7, true, false, now);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 7, 0, 0, iran), weekly.NextRunAtUtc);

        var monthly = new ReportSubscription(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReportFrequency.Monthly, null, 5, 9, true, false, now);
        Assert.Equal(new DateTimeOffset(2026, 12, 5, 9, 0, 0, iran), monthly.NextRunAtUtc);

        // After a send the next one is the following day, not the same hour again.
        daily.MarkSent(daily.NextRunAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 8, 0, 0, iran), daily.NextRunAtUtc);
    }

    [Fact]
    public async Task ASubscription_IsMine_Validated_AndLimited()
    {
        var me = await host.UserAsync(host.TenantA, Permissions);
        var other = await host.UserAsync(host.TenantA, Permissions);
        var created = await host.SendAsync(me, HttpMethod.Post, Url, new { frequency = "Weekly", dayOfWeek = "Saturday", hour = 8, bySms = true });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(me, HttpMethod.Post, Url, new { frequency = "Daily", hour = 30, bySms = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(me, HttpMethod.Post, Url, new { frequency = "Daily", hour = 8, bySms = false, byEmail = false })).StatusCode);

        // Somebody else cannot see, change or delete it.
        Assert.Equal(0, (await Json(await host.SendAsync(other, HttpMethod.Get, Url))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(other, HttpMethod.Put, $"{Url}/{id}", new { frequency = "Daily", hour = 9, bySms = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(other, HttpMethod.Delete, $"{Url}/{id}")).StatusCode);

        for (var i = 0; i < 4; i++) await host.SendAsync(me, HttpMethod.Post, Url, new { frequency = "Daily", hour = 8, bySms = true });
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(me, HttpMethod.Post, Url, new { frequency = "Daily", hour = 8, bySms = true })).StatusCode);
        Assert.True((await host.SendAsync(me, HttpMethod.Delete, $"{Url}/{id}")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task ADueDigest_TextsThePerson_ACountOfTheirOwnTasks_AndIsAdvanced()
    {
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/notification-channels",
            new { sms = new { enabled = true, provider = "test", apiKey = "test-key", lineNumber = "1000092120" } })).StatusCode);
        var me = await host.UserAsync(host.TenantA, Permissions);
        var phone = $"0937{Interlocked.Increment(ref _phone) + 5000000:D7}";
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            db.Users.Find(me.Id)!.UpdateContactDetails(db.Users.Find(me.Id)!.Username, phone, notifySms: true);
            await db.SaveChangesAsync();
        }

        // Two overdue tasks and one in progress, all mine; and one that is someone else's.
        foreach (var title in new[] { "Late one", "Late two" })
            await host.SendAsync(me, HttpMethod.Post, "/api/task-management/tasks", new { title, dueDate = "2020-01-01", priority = "Medium", assignedUserId = me.Id });
        var working = (await Json(await host.SendAsync(me, HttpMethod.Post, "/api/task-management/tasks", new { title = "Working", dueDate = "2090-01-01", priority = "Medium", assignedUserId = me.Id }))).GetProperty("id").GetGuid();
        await host.SendAsync(me, HttpMethod.Patch, $"/api/task-management/tasks/{working}/status", new { status = "InProgress" });
        var stranger = await host.UserAsync(host.TenantA, Permissions);
        await host.SendAsync(stranger, HttpMethod.Post, "/api/task-management/tasks", new { title = "Not mine", dueDate = "2020-01-01", priority = "Medium", assignedUserId = stranger.Id });

        var created = await Json(await host.SendAsync(me, HttpMethod.Post, Url, new { frequency = "Daily", hour = 8, bySms = true }));
        var runner = host.Services.GetRequiredService<ReportDistributionService>();
        var now = DateTimeOffset.UtcNow.AddDays(2);
        Assert.True(await runner.RunOnceAsync(now, default) >= 1);

        var text = Assert.Single(host.Sms.SentTo(phone)).Text;
        Assert.Contains("عقب‌افتاده: 2", text);
        Assert.Contains("در حال انجام: 1", text);
        Assert.Contains("Late one", text);
        Assert.DoesNotContain("Not mine", text);

        // It was advanced: the same pass finds nothing for it now, and the next run is in the future.
        Assert.Equal(0, await runner.RunOnceAsync(now, default));
        var after = (await Json(await host.SendAsync(me, HttpMethod.Get, Url)))[0];
        Assert.True(after.GetProperty("nextRunAtUtc").GetDateTimeOffset() > now);
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("lastSentAtUtc").ValueKind);
        Assert.Equal(created.GetProperty("id").GetGuid(), after.GetProperty("id").GetGuid());
    }
}
