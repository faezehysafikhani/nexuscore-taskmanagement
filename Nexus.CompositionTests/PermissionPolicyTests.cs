using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NexusCore.Application;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure;

namespace Nexus.CompositionTests;

/// <summary>
/// Guards the fix for "AuthorizationPolicy named 'groups.view' was not found" (HTTP 500): every
/// permission an endpoint can name must resolve to a policy, and a name nobody registered must
/// produce a denying policy (403), never an exception.
/// </summary>
public sealed class PermissionPolicyTests
{
    private static ServiceProvider Compose(bool userGroupsEnabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(new DictionaryConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=.;Database=CompositionTests;Trusted_Connection=True;TrustServerCertificate=True",
            ["Features:UserGroups:Enabled"] = userGroupsEnabled ? "true" : "false"
        }));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void GroupPermissions_AreRegisteredAsPolicies_WhenTheFeatureIsOn()
    {
        using var provider = Compose(userGroupsEnabled: true);
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        foreach (var permission in UserGroupPermissions.All)
        {
            // Registered explicitly by AddUserGroupFeature - not merely rescued by the fallback.
            Assert.True(options.GetPolicy(permission.Name) is not null, $"No policy registered for '{permission.Name}'.");
        }
    }

    [Fact]
    public async Task EveryCataloguedPermission_ResolvesToAPolicy()
    {
        using var provider = Compose(userGroupsEnabled: true);
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var catalogued = provider.GetServices<IPermissionCatalog>().SelectMany(c => c.GetPermissions()).Select(p => p.Name).ToList();

        Assert.Contains(UserGroupPermissions.GroupsView, catalogued);
        foreach (var name in catalogued)
        {
            Assert.NotNull(await policies.GetPolicyAsync(name));
        }
    }

    [Fact]
    public async Task UnregisteredPolicyName_GetsAStrictPermissionPolicy_NotNull()
    {
        using var provider = Compose(userGroupsEnabled: false);
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.IsType<PermissionPolicyProvider>(policies);

        var policy = await policies.GetPolicyAsync("no.such.permission");

        // Null here is what used to become a 500. The fallback requires a signed-in user holding
        // exactly that permission claim, so nobody gains access through it.
        Assert.NotNull(policy);
        var requirement = Assert.Single(policy!.Requirements.OfType<PermissionRequirement>());
        Assert.Equal("no.such.permission", requirement.Permission);
        Assert.Contains(policy.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void PersonalWorkTeam_CannotCarryPermissions()
    {
        var team = new UserGroup(Guid.NewGuid(), Guid.NewGuid(), "My team", ownerUserId: Guid.NewGuid());
        Assert.True(team.IsPersonalTeam);
        Assert.Throws<InvalidOperationException>(() => team.SetPermissions([Guid.NewGuid()]));

        var organisational = new UserGroup(Guid.NewGuid(), Guid.NewGuid(), "Finance");
        organisational.SetPermissions([Guid.NewGuid()]);
        Assert.Single(organisational.Permissions);
    }

    /// <summary>Just enough IConfiguration for the composition: flat keys, read by path.</summary>
    private sealed class DictionaryConfiguration(IReadOnlyDictionary<string, string?> values) : IConfiguration
    {
        public string? this[string key]
        {
            get => values.TryGetValue(key, out var value) ? value : null;
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string key) => new Section(this, key);
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

        private sealed class Section(DictionaryConfiguration root, string path) : IConfigurationSection
        {
            public string? this[string key]
            {
                get => root[$"{path}:{key}"];
                set => throw new NotSupportedException();
            }

            public string Key => path.Split(':')[^1];
            public string Path => path;
            public string? Value { get => root[path]; set => throw new NotSupportedException(); }
            public IConfigurationSection GetSection(string key) => new Section(root, $"{path}:{key}");
            public IEnumerable<IConfigurationSection> GetChildren() => [];
            public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
        }
    }
}
