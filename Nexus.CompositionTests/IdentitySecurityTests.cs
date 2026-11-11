using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.CompositionTests;

/// <summary>The SMS code at sign-in, service accounts with API keys, and guests (phase 9b) - through the real endpoints.</summary>
public sealed class IdentitySecurityTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Password = "Zx9#kQ7mWp2$vL";
    private static int _serial = 3000;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task EnableSmsAsync() =>
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/notification-channels",
            new { sms = new { enabled = true, provider = "test", apiKey = "test-key", lineNumber = "1000092120" } })).StatusCode);

    /// <summary>A user who can really sign in: a username (national-code shaped) and a mobile number, with a known password.</summary>
    private async Task<(string Username, string Phone, Guid Id)> NewUserAsync(Guid tenant, bool withPhone = true)
    {
        var n = Interlocked.Increment(ref _serial);
        var username = $"77{n:D8}";
        var phone = $"0935{n + 2000000:D7}";
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
        var user = new User(Guid.NewGuid(), tenant, null, $"Login {n}", scope.ServiceProvider.GetRequiredService<IPasswordHasher>().HashPassword(Password));
        user.UpdateContactDetails(username, withPhone ? phone : null, notifySms: false);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (username, phone, user.Id);
    }

    private Task<HttpResponseMessage> LoginAsync(string username, string password = Password) =>
        host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/login", new { identifier = username, password });

    private async Task<string> CodeSentToAsync(string phone)
    {
        var text = host.Sms.SentTo(phone).Last().Text;
        return new string(text.Where(char.IsDigit).ToArray()).Substring(0, 6);
    }

    private async Task<AccessControlTests.Caller> SignInAsync(string username)
    {
        var body = await Json(await LoginAsync(username));
        return new AccessControlTests.Caller(body.GetProperty("user").GetProperty("id").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    [Fact]
    public async Task WithTheCodeOff_SignInIsAsBefore()
    {
        var (username, _, _) = await NewUserAsync(host.TenantA);
        var body = await Json(await LoginAsync(username));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("mfa").ValueKind);
    }

    [Fact]
    public async Task TurningTheCodeOn_NeedsTheCodeSentToYourPhone_ThenSignInAsksForIt()
    {
        await EnableSmsAsync();
        var (username, phone, _) = await NewUserAsync(host.TenantA);
        var me = await SignInAsync(username);

        var request = await Json(await host.SendAsync(me, HttpMethod.Post, "/api/identity/auth/me/mfa/request-enable"));
        var challenge = request.GetProperty("challengeId").GetGuid();
        Assert.EndsWith(phone[^4..], request.GetProperty("maskedPhone").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(me, HttpMethod.Post, "/api/identity/auth/me/mfa/confirm-enable", new { challengeId = challenge, code = "000000" })).StatusCode);

        var confirmed = await host.SendAsync(me, HttpMethod.Post, "/api/identity/auth/me/mfa/confirm-enable", new { challengeId = challenge, code = await CodeSentToAsync(phone) });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.True((await Json(confirmed)).GetProperty("mfaEnabled").GetBoolean());

        // The password alone is no longer enough: no token, a challenge instead, and a code by SMS.
        var login = await Json(await LoginAsync(username));
        Assert.True(string.IsNullOrEmpty(login.GetProperty("accessToken").GetString()));
        var mfa = login.GetProperty("mfa");
        var id = mfa.GetProperty("challengeId").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/login/mfa", new { challengeId = id, code = "111111" })).StatusCode);
        var done = await Json(await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/login/mfa", new { challengeId = id, code = await CodeSentToAsync(phone) }));
        Assert.False(string.IsNullOrEmpty(done.GetProperty("accessToken").GetString()));

        // A code is used once.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/login/mfa", new { challengeId = id, code = await CodeSentToAsync(phone) })).StatusCode);

        // Turning it off needs the password.
        var signedIn = new AccessControlTests.Caller(done.GetProperty("user").GetProperty("id").GetGuid(), done.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(signedIn, HttpMethod.Post, "/api/identity/auth/me/mfa/disable", new { password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(signedIn, HttpMethod.Post, "/api/identity/auth/me/mfa/disable", new { password = Password })).StatusCode);
        Assert.False(string.IsNullOrEmpty((await Json(await LoginAsync(username))).GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task FiveWrongCodes_KillTheChallenge_EvenForTheRightCode()
    {
        await EnableSmsAsync();
        var (username, phone, userId) = await NewUserAsync(host.TenantA);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            (await db.Users.FindAsync(userId))!.SetMfaEnabled(true);
            await db.SaveChangesAsync();
        }

        var id = (await Json(await LoginAsync(username))).GetProperty("mfa").GetProperty("challengeId").GetGuid();
        var code = await CodeSentToAsync(phone);
        for (var i = 0; i < MfaChallenge.MaxAttempts; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/login/mfa", new { challengeId = id, code = "999999" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/login/mfa", new { challengeId = id, code })).StatusCode);
    }

    [Fact]
    public async Task WhenRequired_EveryoneNeedsIt_AndAnAdministratorCanResetSomeoneWhoLostTheirPhone()
    {
        await EnableSmsAsync();
        var options = host.Services.GetRequiredService<IOptions<MfaOptions>>().Value;
        var (username, phone, userId) = await NewUserAsync(host.TenantA);
        var (noPhoneUser, _, _) = await NewUserAsync(host.TenantA, withPhone: false);
        options.Required = true;
        try
        {
            Assert.NotEqual(JsonValueKind.Null, (await Json(await LoginAsync(username))).GetProperty("mfa").ValueKind);
            // Required but no mobile number: refused with a clear reason (never a silent pass).
            var refused = await LoginAsync(noPhoneUser);
            Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
            Assert.Contains("mfa.no_phone", await refused.Content.ReadAsStringAsync());
        }
        finally { options.Required = false; }

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            (await db.Users.FindAsync(userId))!.SetMfaEnabled(true);
            await db.SaveChangesAsync();
        }

        Assert.True(string.IsNullOrEmpty((await Json(await LoginAsync(username))).GetProperty("accessToken").GetString()));
        var normal = await host.UserAsync(host.TenantA, []);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(normal, HttpMethod.Post, $"/api/identity/users/{userId}/mfa/reset")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(host.Admin, HttpMethod.Post, $"/api/identity/users/{userId}/mfa/reset")).StatusCode);
        Assert.False(string.IsNullOrEmpty((await Json(await LoginAsync(username))).GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task AServiceAccount_SignsInOnlyWithItsKey_AndActsWithItsOwnPermissions()
    {
        var created = await Json(await host.SendAsync(host.Admin, HttpMethod.Post, "/api/identity/service-accounts", new { name = "Reporting robot" }));
        var accountId = created.GetProperty("account").GetProperty("id").GetGuid();
        var key = created.GetProperty("secret").GetString()!;
        Assert.StartsWith("tmk_", key);
        Assert.DoesNotContain(key, (await host.SendAsync(host.Admin, HttpMethod.Get, "/api/identity/service-accounts").Result.Content.ReadAsStringAsync()));

        var token = await Json(await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/api-key/token", new { apiKey = key }));
        var robot = new AccessControlTests.Caller(accountId, token.GetProperty("accessToken").GetString()!);

        // No permissions yet; give it one the usual way and it works.
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(robot, HttpMethod.Get, "/api/task-management/tasks")).StatusCode);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            (await db.Users.FindAsync(accountId))!.SetDirectPermissions(await host.PermissionIdsAsync(TaskManagementPermissions.View));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(robot, HttpMethod.Get, "/api/task-management/tasks")).StatusCode);

        // Wrong or malformed keys get one and the same answer.
        foreach (var bad in new[] { key[..^1] + "0", "tmk_nonsense", "", key.Replace("tmk_", "xxx_") })
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/api-key/token", new { apiKey = bad })).StatusCode);

        // A second key, then the first revoked: only the second works.
        var second = await Json(await host.SendAsync(host.Admin, HttpMethod.Post, $"/api/identity/service-accounts/{accountId}/api-keys", new { name = "ci", expiresInDays = 30 }));
        var firstKeyId = created.GetProperty("account").GetProperty("keys")[0].GetProperty("id").GetGuid();
        Assert.True((await host.SendAsync(host.Admin, HttpMethod.Delete, $"/api/identity/service-accounts/{accountId}/api-keys/{firstKeyId}")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/api-key/token", new { apiKey = key })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/api-key/token", new { apiKey = second.GetProperty("secret").GetString() })).StatusCode);

        // A disabled account's keys stop working at once.
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            (await db.Users.FindAsync(accountId))!.SetActive(false);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(null, HttpMethod.Post, "/api/identity/auth/api-key/token", new { apiKey = second.GetProperty("secret").GetString() })).StatusCode);

        // Only an administrator makes them.
        var normal = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(normal, HttpMethod.Post, "/api/identity/service-accounts", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task Guests_DoNotCountAgainstTheUserLimit_AndAServiceAccountKindCannotBeSetByHand()
    {
        var (_, _, userId) = await NewUserAsync(host.TenantA);
        var usersBefore = (await Json(await host.SendAsync(host.Admin, HttpMethod.Get, "/api/task-management/membership"))).GetProperty("usage").GetProperty("activeUsers").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{userId}/kind", new { kind = "Guest" })).StatusCode);
        var usersAfter = (await Json(await host.SendAsync(host.Admin, HttpMethod.Get, "/api/task-management/membership"))).GetProperty("usage").GetProperty("activeUsers").GetInt32();
        Assert.Equal(usersBefore - 1, usersAfter);

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{userId}/kind", new { kind = "ServiceAccount" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{userId}/kind", new { kind = "Normal" })).StatusCode);
    }
}
