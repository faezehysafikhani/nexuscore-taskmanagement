using System.Net;
using System.Text.Json;
using NexusCore.Application.Ldap;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>HR sync (CAT-9) and sign-in with the directory password: the people of the directory become users, through the real endpoints.</summary>
public sealed class LdapSyncTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Sync = "/api/platform/ldap/sync";
    private static int _serial = 5000;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task EnableLdapAsync() =>
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/ldap",
            new { enabled = true, host = "93.184.216.34", port = 389, userFilter = "(objectClass=person)", connectionTimeoutSeconds = 10 })).StatusCode);

    private async Task<bool> IsActiveAsync(string username)
    {
        var users = await Json(await host.SendAsync(host.Admin, HttpMethod.Get, $"/api/identity/users?search={username}"));
        return users.GetProperty("items")[0].GetProperty("isActive").GetBoolean();
    }

    private LdapUserEntry Person(string? username = null, bool disabled = false, string? phone = null)
    {
        var n = Interlocked.Increment(ref _serial);
        username ??= $"66{n:D8}";
        return new LdapUserEntry($"CN=Person {n},OU=Staff,DC=corp,DC=example", username, $"Person {n}", "First", $"Last {n}", $"p{n}@corp.example", phone ?? $"0936{n + 3000000:D7}", disabled);
    }

    [Fact]
    public async Task PreviewChangesNothing_ApplyCreatesTheUsers_AndTheyCanSignInWithTheirDirectoryPassword()
    {
        await EnableLdapAsync();
        host.Ldap.Entries.Clear();
        var good = Person();
        var notANationalCode = Person("jsmith");
        var disabled = Person(disabled: true);
        host.Ldap.Entries.AddRange([good, notANationalCode, disabled]);
        host.Ldap.Passwords[good.Dn] = "Dir-Pass-1";

        var preview = await Json(await host.SendAsync(host.Admin, HttpMethod.Post, $"{Sync}/preview", new { }));
        Assert.False(preview.GetProperty("applied").GetBoolean());
        Assert.Equal(1, preview.GetProperty("created").GetInt32());
        Assert.Equal(2, preview.GetProperty("skipped").GetInt32());
        // Nothing was made yet: the directory person is not a user until the sync is applied.
        var users = await Json(await host.SendAsync(host.Admin, HttpMethod.Get, $"/api/identity/users?search={good.Username}"));
        Assert.Equal(0, users.GetProperty("items").GetArrayLength());

        var applied = await Json(await host.SendAsync(host.Admin, HttpMethod.Post, Sync, new { }));
        Assert.True(applied.GetProperty("applied").GetBoolean());
        Assert.Equal(1, applied.GetProperty("created").GetInt32());
        Assert.Contains(applied.GetProperty("rows").EnumerateArray(), r => r.GetProperty("username").GetString() == "jsmith" && r.GetProperty("action").GetString() == "Skipped");

        var login = await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/login", new { identifier = good.Username, password = "Dir-Pass-1" });
        Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty((await Json(login)).GetProperty("accessToken").GetString()));
        Assert.NotEqual(HttpStatusCode.OK, (await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/login", new { identifier = good.Username, password = "wrong" })).StatusCode);

        // Running it again finds nothing to change.
        var again = await Json(await host.SendAsync(host.Admin, HttpMethod.Post, Sync, new { }));
        Assert.Equal(0, again.GetProperty("created").GetInt32());
        Assert.Equal(1, again.GetProperty("unchanged").GetInt32());
    }

    [Fact]
    public async Task APersonWhoLeavesOrIsDisabled_IsSwitchedOff_AndComesBackWhenTheyReturn()
    {
        await EnableLdapAsync();
        host.Ldap.Entries.Clear();
        var person = Person();
        host.Ldap.Entries.Add(person);
        host.Ldap.Passwords[person.Dn] = "Dir-Pass-2";
        Assert.Equal(1, (await Json(await host.SendAsync(host.Admin, HttpMethod.Post, Sync, new { }))).GetProperty("created").GetInt32());

        // Disabled in the directory.
        host.Ldap.Entries[0] = person with { IsDisabled = true };
        var off = await Json(await host.SendAsync(host.Admin, HttpMethod.Post, Sync, new { }));
        Assert.Equal(1, off.GetProperty("deactivated").GetInt32());
        Assert.False(await IsActiveAsync(person.Username!));

        // Back, then gone from the directory altogether (another person stays so the directory is not empty).
        host.Ldap.Entries[0] = person;
        Assert.Equal(1, (await Json(await host.SendAsync(host.Admin, HttpMethod.Post, Sync, new { }))).GetProperty("reactivated").GetInt32());
        host.Ldap.Entries.Clear();
        host.Ldap.Entries.Add(Person());
        var gone = await Json(await host.SendAsync(host.Admin, HttpMethod.Post, Sync, new { }));
        Assert.True(gone.GetProperty("deactivated").GetInt32() >= 1);

        // An empty answer is not taken as "everybody left".
        host.Ldap.Entries.Clear();
        Assert.Equal(0, (await Json(await host.SendAsync(host.Admin, HttpMethod.Post, Sync, new { }))).GetProperty("deactivated").GetInt32());
    }

    [Fact]
    public async Task TheUserLimit_IsRespected_LocalUsersAreNotTouched_AndItNeedsThePermission()
    {
        await EnableLdapAsync();
        var local = await host.UserAsync(host.TenantA, []);
        var users = (await Json(await host.SendAsync(host.Admin, HttpMethod.Get, "/api/task-management/membership"))).GetProperty("usage").GetProperty("activeUsers").GetInt32();
        await host.SendAsync(host.Admin, HttpMethod.Put, "/api/task-management/membership", new { planName = "Full", maxUsers = users });
        try
        {
            host.Ldap.Entries.Clear();
            host.Ldap.Entries.Add(Person());
            var report = await Json(await host.SendAsync(host.Admin, HttpMethod.Post, Sync, new { }));
            Assert.Equal(0, report.GetProperty("created").GetInt32());
            Assert.Contains("سقف کاربران", report.GetProperty("rows")[0].GetProperty("reason").GetString());
        }
        finally { await host.SendAsync(host.Admin, HttpMethod.Delete, "/api/task-management/membership"); }

        Assert.True(await host.UserIsActiveAsync(local.Id));
        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(viewer, HttpMethod.Post, Sync, new { })).StatusCode);

        host.Ldap.Fail = true;
        try { Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(host.Admin, HttpMethod.Post, $"{Sync}/preview", new { })).StatusCode); }
        finally { host.Ldap.Fail = false; }
    }
}
