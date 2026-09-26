using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Permissions;
using Nexus.TaskManagement.Realtime;
using NexusCore.Application.Identity.Services;
using NexusCore.Infrastructure.Persistence;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>
/// Online/offline comes from real hub connections: the hub's own connect and disconnect run
/// here (with the host's tracker), and presence is read through the real endpoint. Online while
/// at least one connection is open, offline when the last one closes; never for a disabled user
/// or for another organization.
/// </summary>
public sealed class PresenceTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] Permissions = [TaskManagementPermissions.View];

    [Fact]
    public async Task OneConnection_MakesTheUserOnline_AndClosingIt_Offline()
    {
        var viewer = await host.UserAsync(host.TenantA, Permissions);
        var user = await host.UserAsync(host.TenantA, Permissions);
        Assert.False(await IsOnlineAsync(viewer, user.Id));

        var tab = await ConnectAsync(user);
        Assert.True(await IsOnlineAsync(viewer, user.Id));

        await tab.CloseAsync();
        Assert.False(await IsOnlineAsync(viewer, user.Id));
    }

    [Fact]
    public async Task SeveralTabs_KeepTheUserOnline_UntilTheLastOneCloses_AndAReconnectCounts()
    {
        var viewer = await host.UserAsync(host.TenantA, Permissions);
        var user = await host.UserAsync(host.TenantA, Permissions);

        var first = await ConnectAsync(user);
        var second = await ConnectAsync(user);
        await first.CloseAsync();
        Assert.True(await IsOnlineAsync(viewer, user.Id));

        await second.CloseAsync();
        Assert.False(await IsOnlineAsync(viewer, user.Id));

        // Reconnecting (a new connection id, as SignalR's automatic reconnect opens) is online again.
        var reconnected = await ConnectAsync(user);
        Assert.True(await IsOnlineAsync(viewer, user.Id));
        await reconnected.CloseAsync();
    }

    [Fact]
    public async Task ADisabledUser_IsNeverOnline_EvenWithAnOpenConnection()
    {
        var viewer = await host.UserAsync(host.TenantA, Permissions);
        var user = await host.UserAsync(host.TenantA, Permissions);
        var tab = await ConnectAsync(user);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            (await db.Users.SingleAsync(u => u.Id == user.Id)).SetActive(false);
            await db.SaveChangesAsync();
        }

        Assert.False(await IsOnlineAsync(viewer, user.Id));
        await tab.CloseAsync();
    }

    [Fact]
    public async Task AnotherOrganizationsUser_IsNotReported()
    {
        var viewer = await host.UserAsync(host.TenantA, Permissions);
        var stranger = await host.UserAsync(host.TenantB, Permissions);
        var tab = await ConnectAsync(stranger);

        var presence = await PresenceAsync(viewer, stranger.Id);

        Assert.Empty(presence.EnumerateArray());
        await tab.CloseAsync();
    }

    [Fact]
    public async Task Presence_NeedsASignedInUser()
    {
        var response = await host.SendAsync(new AccessControlTests.Caller(Guid.Empty, "not-a-token"), HttpMethod.Get, $"/api/identity/presence?userIds={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<bool> IsOnlineAsync(AccessControlTests.Caller viewer, Guid userId)
    {
        var entry = Assert.Single((await PresenceAsync(viewer, userId)).EnumerateArray());
        Assert.Equal(userId, entry.GetProperty("userId").GetGuid());
        return entry.GetProperty("isOnline").GetBoolean();
    }

    private async Task<JsonElement> PresenceAsync(AccessControlTests.Caller viewer, Guid userId)
    {
        var response = await host.SendAsync(viewer, HttpMethod.Get, $"/api/identity/presence?userIds={userId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    /// <summary>Opens a connection of the user's through the task-management hub, as a browser tab does.</summary>
    private async Task<OpenConnection> ConnectAsync(AccessControlTests.Caller caller)
    {
        Guid tenantId;
        using (var scope = host.Services.CreateScope())
        {
            tenantId = (await scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == caller.Id)).TenantId;
        }

        var context = new FakeCallerContext(Guid.NewGuid().ToString("N"));
        var connection = new OpenConnection(host.Services.GetRequiredService<IUserPresenceTracker>(), new SignedIn(caller.Id, tenantId), context);
        await connection.OpenAsync();
        return connection;
    }

    private sealed class OpenConnection(IUserPresenceTracker tracker, ICurrentUserContext user, HubCallerContext context)
    {
        private TaskManagementHub NewHub() => new(user, tracker) { Context = context, Groups = new NoGroups() };

        public Task OpenAsync() => NewHub().OnConnectedAsync();

        // SignalR creates a new hub instance for each call; the connection's items carry over.
        public Task CloseAsync() => NewHub().OnDisconnectedAsync(null);
    }

    private sealed class SignedIn(Guid userId, Guid tenantId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public Guid? TenantId => tenantId;
        public string? Email => null;
        public string? IpAddress => null;
        public bool HasPermission(string permission) => false;
    }

    private sealed class FakeCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class NoGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
