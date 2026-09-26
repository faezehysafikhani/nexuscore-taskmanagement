using NexusCore.Domain.Identity;

namespace NexusCore.Tests.Identity;

public sealed class IdentityDomainTests
{
    [Fact]
    public void User_SetRoles_ReplacesExistingAssignments()
    {
        var tenantId = Guid.NewGuid();
        var user = new User(Guid.NewGuid(), tenantId, "USER@Example.COM", "Test User", "hash");
        var firstRole = Guid.NewGuid();
        var secondRole = Guid.NewGuid();

        user.AssignRole(firstRole);
        user.SetRoles([secondRole]);

        Assert.Single(user.Roles);
        Assert.Equal(secondRole, user.Roles.Single().RoleId);
        Assert.Equal("user@example.com", user.Email);
    }

    [Fact]
    public void Role_SetPermissions_DeduplicatesPermissions()
    {
        var role = new Role(Guid.NewGuid(), Guid.NewGuid(), "Admin");
        var permissionId = Guid.NewGuid();

        role.SetPermissions([permissionId, permissionId]);

        Assert.Single(role.Permissions);
        Assert.Equal(permissionId, role.Permissions.Single().PermissionId);
    }
}

public sealed class UserPresenceTrackerTests
{
    [Fact]
    public void AUserIsOnline_WhileAnyOfTheirConnectionsIsOpen()
    {
        var tracker = new NexusCore.Application.Identity.Services.InMemoryUserPresenceTracker();
        var (user, other) = (Guid.NewGuid(), Guid.NewGuid());

        tracker.Connected(user, "tab-1");
        tracker.Connected(user, "tab-2");
        tracker.Disconnected(user, "tab-1");
        Assert.True(tracker.IsOnline(user));
        Assert.False(tracker.IsOnline(other));

        tracker.Disconnected(user, "tab-2");
        Assert.False(tracker.IsOnline(user));

        // A connection closing twice, or one never opened, changes nothing.
        tracker.Disconnected(user, "tab-2");
        tracker.Disconnected(other, "x");
        tracker.Connected(user, "tab-3");
        Assert.True(tracker.IsOnline(user));
    }

    [Fact]
    public void ManyConnectionsOpeningAndClosingAtOnce_LeaveTheRightState()
    {
        var tracker = new NexusCore.Application.Identity.Services.InMemoryUserPresenceTracker();
        var user = Guid.NewGuid();

        Parallel.For(0, 500, i =>
        {
            tracker.Connected(user, $"c{i}");
            if (i % 2 == 0) tracker.Disconnected(user, $"c{i}");
        });
        Assert.True(tracker.IsOnline(user));

        Parallel.For(0, 500, i => tracker.Disconnected(user, $"c{i}"));
        Assert.False(tracker.IsOnline(user));
    }
}
