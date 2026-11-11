using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Infrastructure;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Identity.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>The organization's plan (phase 9): usage report, limits that are enforced, and expiry.</summary>
public sealed class MembershipTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Url = "/api/task-management/membership";
    private static readonly string[] Permissions = [TaskManagementPermissions.View, TaskManagementPermissions.ViewMembership, TaskManagementPermissions.ManageMembership];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(offset)).ToString("yyyy-MM-dd");

    private async Task<string?> UserCheckAsync(Guid tenant)
    {
        using var scope = host.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IUserQuota>().CheckCanAddUserAsync(tenant, default))?.Message;
    }

    [Fact]
    public async Task WithNoPlan_NothingIsLimited_AndTheReportShowsTheUsage()
    {
        var admin = await host.UserAsync(host.TenantA, Permissions);
        await host.SendAsync(admin, HttpMethod.Delete, Url);
        var report = await Json(await host.SendAsync(admin, HttpMethod.Get, Url));
        Assert.Equal("NoPlan", report.GetProperty("status").GetString());
        Assert.True(report.GetProperty("usage").GetProperty("activeUsers").GetInt32() >= 1);
        Assert.Null(await UserCheckAsync(host.TenantA));
    }

    [Fact]
    public async Task TheUserLimit_StopsNewUsers_AndTheReportWarns()
    {
        var admin = await host.UserAsync(host.TenantA, Permissions);
        var users = (await Json(await host.SendAsync(admin, HttpMethod.Get, Url))).GetProperty("usage").GetProperty("activeUsers").GetInt32();

        var full = await Json(await host.SendAsync(admin, HttpMethod.Put, Url, new { planName = "Free", maxUsers = users, validUntil = Day(200) }));
        Assert.Equal("Active", full.GetProperty("status").GetString());
        Assert.Contains("limit is reached", full.GetProperty("warnings").ToString());
        Assert.Contains("سقف کاربران", await UserCheckAsync(host.TenantA));

        await host.SendAsync(admin, HttpMethod.Put, Url, new { planName = "Pro", maxUsers = users + 5, validUntil = Day(200) });
        Assert.Null(await UserCheckAsync(host.TenantA));
        await host.SendAsync(admin, HttpMethod.Delete, Url);
    }

    [Fact]
    public async Task IdentityItself_RefusesANewUser_WhenThePlanIsFull()
    {
        var admin = await host.UserAsync(host.TenantA, Permissions);
        var users = (await Json(await host.SendAsync(admin, HttpMethod.Get, Url))).GetProperty("usage").GetProperty("activeUsers").GetInt32();
        await host.SendAsync(admin, HttpMethod.Put, Url, new { planName = "Full", maxUsers = users });
        try
        {
            var refused = await host.SendAsync(host.Admin, HttpMethod.Post, "/api/identity/users", new
            {
                tenantId = host.TenantA, username = "0010350829", firstName = "Plan", lastName = "Limit", phoneNumber = "09121234567", password = "Zx9#kQ7mWp2$vL"
            });
            Assert.True(refused.StatusCode == HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
            Assert.Contains("سقف کاربران", await refused.Content.ReadAsStringAsync());
        }
        finally
        {
            await host.SendAsync(admin, HttpMethod.Delete, Url);
        }
    }

    [Fact]
    public async Task AnExpiredMembership_RefusesUsersAndFiles_AndAnEndingOneWarns()
    {
        var admin = await host.UserAsync(host.TenantA, Permissions);
        var expired = await Json(await host.SendAsync(admin, HttpMethod.Put, Url, new { planName = "Old", validFrom = Day(-90), validUntil = Day(-1) }));
        Assert.Equal("Expired", expired.GetProperty("status").GetString());
        Assert.Contains("اشتراک", await UserCheckAsync(host.TenantA));
        using (var scope = host.Services.CreateScope())
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IMembershipService>().CheckStorageAsync(host.TenantA, 10, default));

        var ending = await Json(await host.SendAsync(admin, HttpMethod.Put, Url, new { planName = "Ending", validUntil = Day(5) }));
        Assert.Equal("ExpiringSoon", ending.GetProperty("status").GetString());
        Assert.InRange(ending.GetProperty("daysLeft").GetInt32(), 4, 5);
        Assert.Null(await UserCheckAsync(host.TenantA));

        var future = await Json(await host.SendAsync(admin, HttpMethod.Put, Url, new { planName = "Soon", validFrom = Day(10) }));
        Assert.Equal("NotStarted", future.GetProperty("status").GetString());
        await host.SendAsync(admin, HttpMethod.Delete, Url);
    }

    [Fact]
    public async Task TheStorageLimit_RefusesFilesThatWouldGoOver()
    {
        var admin = await host.UserAsync(host.TenantA, Permissions);
        await host.SendAsync(admin, HttpMethod.Put, Url, new { planName = "Tiny", maxStorageMb = 1 });
        using var scope = host.Services.CreateScope();
        var membership = scope.ServiceProvider.GetRequiredService<IMembershipService>();
        Assert.Null(await membership.CheckStorageAsync(host.TenantA, 100, default));
        Assert.Contains("سقف فضا", (await membership.CheckStorageAsync(host.TenantA, 2L * 1024 * 1024, default))?.Message);
        await host.SendAsync(admin, HttpMethod.Delete, Url);
    }

    [Fact]
    public async Task ViewingAndManaging_AreSeparatePermissions_AndBadInputIsRefused()
    {
        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.ViewMembership]);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(viewer, HttpMethod.Get, Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(viewer, HttpMethod.Put, Url, new { planName = "x" })).StatusCode);
        var nobody = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(nobody, HttpMethod.Get, Url)).StatusCode);

        var admin = await host.UserAsync(host.TenantA, Permissions);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(admin, HttpMethod.Put, Url, new { planName = "", maxUsers = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(admin, HttpMethod.Put, Url, new { planName = "x", maxUsers = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(admin, HttpMethod.Put, Url, new { planName = "x", validFrom = Day(5), validUntil = Day(1) })).StatusCode);
    }
}

/// <summary>Retention of the module's own logs (phase 9).</summary>
public sealed class RetentionTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Url = "/api/task-management/membership/retention";
    private static readonly string[] Permissions =
        [Nexus.TaskManagement.Permissions.TaskManagementPermissions.View, Nexus.TaskManagement.Permissions.TaskManagementPermissions.ViewMembership,
         Nexus.TaskManagement.Permissions.TaskManagementPermissions.ManageMembership, Nexus.TaskManagement.Permissions.TaskManagementPermissions.ManageAutomation,
         Nexus.TaskManagement.Permissions.TaskManagementPermissions.Create];

    [Fact]
    public async Task OldLogs_AreRemoved_AndRecentOnesKept_OnlyForOrganizationsThatChoseARetention()
    {
        var admin = await host.UserAsync(host.TenantA, Permissions);
        var other = await host.UserAsync(host.TenantB, Permissions);
        // An automation rule that logs on every new task, in both organizations.
        foreach (var u in new[] { admin, other })
            Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(u, HttpMethod.Post, "/api/task-management/automation/rules",
                new { name = "Log", trigger = "TaskCreated", actions = new object[] { new { type = "AddTag", tag = "x" } } })).StatusCode);
        foreach (var u in new[] { admin, other })
            await host.SendAsync(u, HttpMethod.Post, "/api/task-management/tasks/quick", new { title = "Logged" });

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(admin, HttpMethod.Put, Url, new { automationLogDays = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(admin, HttpMethod.Put, Url, new { automationLogDays = 30 })).StatusCode);
        Assert.Equal(30, JsonDocument.Parse(await (await host.SendAsync(admin, HttpMethod.Get, Url)).Content.ReadAsStringAsync()).RootElement.GetProperty("automationLogDays").GetInt32());

        var purger = host.Services.GetRequiredService<Nexus.TaskManagement.Infrastructure.TaskRetentionBackgroundService>();
        Assert.Equal(0, await purger.PurgeAsync(DateTimeOffset.UtcNow, default));
        // 40 days later: the first organization's logs are older than its 30 days; the other kept everything.
        Assert.True(await purger.PurgeAsync(DateTimeOffset.UtcNow.AddDays(40), default) >= 1);

        static async Task<int> LogCount(AccessControlTests.Host h, AccessControlTests.Caller c) =>
            JsonDocument.Parse(await (await h.SendAsync(c, HttpMethod.Get, "/api/task-management/automation/log")).Content.ReadAsStringAsync()).RootElement.GetArrayLength();
        Assert.Equal(0, await LogCount(host, admin));
        Assert.True(await LogCount(host, other) >= 1);
    }
}
