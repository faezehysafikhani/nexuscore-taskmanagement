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
