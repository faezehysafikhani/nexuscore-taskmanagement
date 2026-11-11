using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>Webhooks (phase 9): subscriptions, the outbox, signed delivery and the address rules.</summary>
public sealed class WebhookTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Hooks = "/api/task-management/webhooks";
    private const string PublicUrl = "https://93.184.216.34/hook";
    private static readonly string[] Permissions =
        [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit, TaskManagementPermissions.ManageWebhooks];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(Guid Id, string Secret)> AddAsync(AccessControlTests.Caller admin, string[]? events = null, string url = PublicUrl)
    {
        var response = await host.SendAsync(admin, HttpMethod.Post, Hooks, new { name = "Hook " + Guid.NewGuid().ToString("N")[..5], url, events });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        return (body.GetProperty("webhook").GetProperty("id").GetGuid(), body.GetProperty("secret").GetString()!);
    }

    private sealed class FakeReceiver(HttpStatusCode answer) : HttpMessageHandler
    {
        public List<(string Body, string Signature, string Event, string Delivery)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add((await request.Content!.ReadAsStringAsync(cancellationToken), request.Headers.GetValues("X-TM-Signature").Single(),
                request.Headers.GetValues("X-TM-Event").Single(), request.Headers.GetValues("X-TM-Delivery").Single()));
            return new HttpResponseMessage(answer);
        }
    }

    private async Task<int> DeliverAsync(FakeReceiver receiver)
    {
        var service = host.Services.GetRequiredService<WebhookDeliveryService>();
        return await service.DeliverDueAsync(new HttpClient(receiver), 500, default);
    }

    [Fact]
    public async Task ASecret_IsShownOnce_AndTheAddressRulesAreEnforced()
    {
        var admin = await host.UserAsync(host.TenantA, Permissions);
        var (id, secret) = await AddAsync(admin);
        Assert.StartsWith("whsec_", secret);

        var list = await Json(await host.SendAsync(admin, HttpMethod.Get, Hooks));
        Assert.DoesNotContain(secret, list.ToString());
        Assert.Contains(list.EnumerateArray(), w => w.GetProperty("id").GetGuid() == id);

        foreach (var bad in new[] { "http://93.184.216.34/hook", "https://127.0.0.1/hook", "https://10.1.2.3/hook", "https://169.254.169.254/latest", "https://user:pw@93.184.216.34/x", "not a url" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(admin, HttpMethod.Post, Hooks, new { name = "bad", url = bad })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(admin, HttpMethod.Post, Hooks, new { name = "bad", url = PublicUrl, events = new[] { "nope" } })).StatusCode);

        var rotated = await Json(await host.SendAsync(admin, HttpMethod.Post, $"{Hooks}/{id}/rotate-secret"));
        Assert.NotEqual(secret, rotated.GetProperty("secret").GetString());
        Assert.True((await host.SendAsync(admin, HttpMethod.Delete, $"{Hooks}/{id}")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task ManagingWebhooks_NeedsThePermission()
    {
        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(viewer, HttpMethod.Get, Hooks)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(viewer, HttpMethod.Post, Hooks, new { name = "x", url = PublicUrl })).StatusCode);
    }

    [Fact]
    public async Task TaskEvents_AreQueued_ThenDeliveredSigned_ToOnlyTheSubscriptionsThatWantThem()
    {
        var admin = await host.UserAsync(host.TenantB, Permissions);
        var (all, secret) = await AddAsync(admin);
        var (completedOnly, _) = await AddAsync(admin, ["task.completed"]);

        var created = await host.SendAsync(admin, HttpMethod.Post, "/api/task-management/tasks/quick", new { title = "Ship the webhook" });
        var taskId = (await Json(created)).GetProperty("id").GetGuid();
        await host.SendAsync(admin, HttpMethod.Patch, $"/api/task-management/tasks/{taskId}/status", new { status = "Completed" });

        var receiver = new FakeReceiver(HttpStatusCode.OK);
        Assert.True(await DeliverAsync(receiver) >= 3);

        // The catch-all one got created + status_changed + completed; the other only completed.
        var mine = receiver.Seen.Where(s => JsonDocument.Parse(s.Body).RootElement.GetProperty("data").GetProperty("taskId").GetGuid() == taskId).ToList();
        Assert.Equal(["task.completed", "task.created", "task.status_changed"], mine.Select(x => x.Event).Distinct().Order().ToArray());
        var deliveries = await Json(await host.SendAsync(admin, HttpMethod.Get, $"{Hooks}/{all}/deliveries"));
        Assert.True(deliveries.GetArrayLength() >= 3);
        Assert.All(deliveries.EnumerateArray(), d => Assert.True(d.GetProperty("delivered").GetBoolean()));
        var only = await Json(await host.SendAsync(admin, HttpMethod.Get, $"{Hooks}/{completedOnly}/deliveries"));
        Assert.All(only.EnumerateArray(), d => Assert.Equal("task.completed", d.GetProperty("eventType").GetString()));

        // The signature is the HMAC of the body with the secret; the receiver can check it.
        var one = mine.First(s => s.Event == "task.created");
        Assert.Equal(WebhookSigner.Sign(secret, one.Body), one.Signature);
        Assert.Equal("Ship the webhook", JsonDocument.Parse(one.Body).RootElement.GetProperty("data").GetProperty("title").GetString());
    }

    [Fact]
    public async Task AFailedDelivery_IsKept_ForARetry_AndAPingWorks()
    {
        var admin = await host.UserAsync(host.TenantB, Permissions);
        var (id, _) = await AddAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(admin, HttpMethod.Post, $"{Hooks}/{id}/ping")).StatusCode);

        var failing = new FakeReceiver(HttpStatusCode.InternalServerError);
        await DeliverAsync(failing);
        var rows = await Json(await host.SendAsync(admin, HttpMethod.Get, $"{Hooks}/{id}/deliveries"));
        var ping = rows.EnumerateArray().First(r => r.GetProperty("eventType").GetString() == "ping");
        Assert.False(ping.GetProperty("delivered").GetBoolean());
        Assert.False(ping.GetProperty("gaveUp").GetBoolean());
        Assert.Equal(1, ping.GetProperty("attempts").GetInt32());
        Assert.Equal(500, ping.GetProperty("lastStatusCode").GetInt32());
    }

    [Fact]
    public void RetryAndGiveUp_AndTheSwitchOff_FollowTheirRules()
    {
        var now = DateTimeOffset.UtcNow;
        var delivery = new WebhookDelivery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ping", "{}", now);
        delivery.Failed(500, "x", now);
        Assert.True(delivery.IsPending);
        Assert.Equal(now.AddSeconds(30), delivery.NextAttemptAtUtc);
        delivery.Failed(500, "x", now);
        Assert.Equal(now.AddSeconds(60), delivery.NextAttemptAtUtc);
        for (var i = 2; i < WebhookDelivery.MaxAttempts; i++) delivery.Failed(null, "x", now);
        Assert.False(delivery.IsPending);
        Assert.NotNull(delivery.GaveUpAtUtc);

        var hook = new WebhookSubscription(Guid.NewGuid(), Guid.NewGuid(), "h", PublicUrl, "s", "*");
        for (var i = 0; i < WebhookSubscription.MaxConsecutiveFailures; i++) hook.RecordFailure();
        Assert.False(hook.IsActive);
        Assert.True(hook.Wants("anything") == false);
    }

    [Theory]
    [InlineData("93.184.216.34", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("172.16.9.9", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("2606:4700:4700::1111", true)]
    public void OnlyPublicAddresses_AreAllowed(string address, bool allowed) =>
        Assert.Equal(allowed, WebhookAddressRules.IsPublic(IPAddress.Parse(address)));
}
