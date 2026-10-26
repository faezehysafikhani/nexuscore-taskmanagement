using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NexusCore.Application;
using NexusCore.Application.Endpoints;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Identity.Validators;
using NexusCore.Application.Messaging;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure;
using NexusCore.Infrastructure.Persistence;
using NexusCore.Infrastructure.Security;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Tests.Identity;

/// <summary>
/// The sign-in session: refresh token rotation and reuse detection, the HttpOnly cookie, ending
/// other sessions, the SMS-verified mobile number change, and acting on higher-privileged users.
/// </summary>
public sealed class SessionSecurityTests
{
    private const string Password = "Correct-Pass-1";
    private const string UserName = "0012345678";
    private const string UserPhone = "09121234567";

    // ---------------------------------------------------------------- refresh token rotation

    [Fact]
    public async Task Refresh_GivesANewToken_AndTheSignInKeepsItsLastDay()
    {
        await using var api = await Api.StartAsync();
        var first = await Json(await api.LoginAsync(UserName, Password));

        var second = await Json(await api.RefreshAsync(first.GetProperty("refreshToken").GetString()!));

        Assert.NotEqual(first.GetProperty("refreshToken").GetString(), second.GetProperty("refreshToken").GetString());
        // A refresh does not stretch the session: both tokens end on the same day.
        Assert.Equal(first.GetProperty("refreshTokenExpiresAtUtc").GetDateTimeOffset(), second.GetProperty("refreshTokenExpiresAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task AnExchangedToken_ShownAgainAfterTheGraceWindow_EndsTheWholeSignIn()
    {
        await using var api = await Api.StartAsync(reuseGraceSeconds: 0);
        var first = await Json(await api.LoginAsync(UserName, Password));
        var oldToken = first.GetProperty("refreshToken").GetString()!;
        var newToken = (await Json(await api.RefreshAsync(oldToken))).GetProperty("refreshToken").GetString()!;

        // The thief (or the stale tab) comes back with the token that was already exchanged.
        var replay = await api.RefreshAsync(oldToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        // The honest token of the same sign-in is revoked as well, and the reuse is on record.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.RefreshAsync(newToken)).StatusCode);
        Assert.Contains("identity.refresh_token_reuse", await api.AuditActionsAsync());
    }

    [Fact]
    public async Task AnExchangedToken_ShownAgainWithinTheGraceWindow_IsAnHonestDuplicate()
    {
        await using var api = await Api.StartAsync(reuseGraceSeconds: 60);
        var oldToken = (await Json(await api.LoginAsync(UserName, Password))).GetProperty("refreshToken").GetString()!;
        var tabOne = await Json(await api.RefreshAsync(oldToken));

        var tabTwo = await api.RefreshAsync(oldToken);

        Assert.Equal(HttpStatusCode.OK, tabTwo.StatusCode);
        var tabTwoToken = (await Json(tabTwo)).GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(tabOne.GetProperty("refreshToken").GetString(), tabTwoToken);
        // Both descendants work.
        Assert.Equal(HttpStatusCode.OK, (await api.RefreshAsync(tabOne.GetProperty("refreshToken").GetString()!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.RefreshAsync(tabTwoToken)).StatusCode);
    }

    [Fact]
    public async Task ASignedOutToken_IsJustInvalid_NotAnAttack()
    {
        await using var api = await Api.StartAsync(reuseGraceSeconds: 0);
        var token = (await Json(await api.LoginAsync(UserName, Password))).GetProperty("refreshToken").GetString()!;
        await api.SendAsync(HttpMethod.Post, "/api/identity/auth/logout", null, new { refreshToken = token });

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.RefreshAsync(token)).StatusCode);
        Assert.DoesNotContain("identity.refresh_token_reuse", await api.AuditActionsAsync());
    }

    [Fact]
    public async Task TheRefreshTokenRemembersWhereItWasIssued()
    {
        await using var api = await Api.StartAsync();
        await api.LoginAsync(UserName, Password);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
        var token = await db.RefreshTokens.AsNoTracking().FirstAsync();
        Assert.NotNull(token.FamilyId);
    }

    // ---------------------------------------------------------------- the cookie

    [Fact]
    public async Task ABrowserThatAsksForTheCookie_NeverSeesTheRefreshTokenInTheBody()
    {
        await using var api = await Api.StartAsync();

        var response = await api.LoginAsync(UserName, Password, cookieMode: true);

        var body = await Json(response);
        Assert.True(string.IsNullOrEmpty(body.GetProperty("refreshToken").GetString()));
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("nexus_rt=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/identity/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ByDefault_EveryClientGetsTheCookieOnly_AndTheBodyTokenIsNotAccepted()
    {
        await using var api = await Api.StartAsync(tokenInBody: false);

        var response = await api.LoginAsync(UserName, Password);

        Assert.True(string.IsNullOrEmpty((await Json(response)).GetProperty("refreshToken").GetString()));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("nexus_rt=", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.RefreshAsync("anything")).StatusCode);
    }

    [Fact]
    public async Task OtherClients_StillGetTheTokenInTheBody_WhenTheTransitionSwitchIsOn()
    {
        await using var api = await Api.StartAsync();

        var response = await api.LoginAsync(UserName, Password);

        Assert.False(string.IsNullOrEmpty((await Json(response)).GetProperty("refreshToken").GetString()));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Refresh_FromTheCookie_NeedsTheCsrfHeader()
    {
        await using var api = await Api.StartAsync();
        var cookieValue = CookieValue(await api.LoginAsync(UserName, Password, cookieMode: true));

        var withoutHeader = await api.CookieRefreshAsync(cookieValue, csrfHeader: false);
        var withHeader = await api.CookieRefreshAsync(cookieValue, csrfHeader: true);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutHeader.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withHeader.StatusCode);
        Assert.True(string.IsNullOrEmpty((await Json(withHeader)).GetProperty("refreshToken").GetString()));
        Assert.Contains(withHeader.Headers.GetValues("Set-Cookie"), value => value.StartsWith("nexus_rt=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logout_ClearsTheCookie_AndEndsTheSession()
    {
        await using var api = await Api.StartAsync();
        var cookieValue = CookieValue(await api.LoginAsync(UserName, Password, cookieMode: true));

        var logout = await api.CookieSendAsync(HttpMethod.Post, "/api/identity/auth/logout", cookieValue, null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), value => value.StartsWith("nexus_rt=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CookieRefreshAsync(cookieValue, csrfHeader: true)).StatusCode);
    }

    // ---------------------------------------------------------------- password change

    [Fact]
    public async Task ChangingThePassword_EndsTheOtherSessions_ButNotTheCurrentOne()
    {
        await using var api = await Api.StartAsync();
        var current = await api.LoginAsync(UserName, Password, cookieMode: true);
        var currentAccess = (await Json(current)).GetProperty("accessToken").GetString()!;
        var currentCookie = CookieValue(current);
        var other = (await Json(await api.LoginAsync(UserName, Password))).GetProperty("refreshToken").GetString()!;

        var change = await api.CookieSendAsync(HttpMethod.Put, "/api/identity/auth/me/password", currentCookie,
            new { currentPassword = Password, newPassword = "Brand-New-Pass-9" }, accessToken: currentAccess);

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.RefreshAsync(other)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.CookieRefreshAsync(currentCookie, csrfHeader: true)).StatusCode);
    }

    [Theory]
    [InlineData("short1A!")]            // too short
    [InlineData("alllowercase123")]     // two kinds of characters
    [InlineData("Password123")]         // common
    [InlineData("Admin@12345")]         // common, and 11 characters
    [InlineData("aaaaaaAAAAA1")]        // hardly any different characters
    public void WeakPasswords_AreRefused(string password) => Assert.False(PasswordRules.IsStrong(password));

    [Theory]
    [InlineData("Correct-Pass-1")]
    [InlineData("rEd7-horse-battery")]
    [InlineData("Tejarat#Staff2026")]
    public void StrongPasswords_AreAccepted(string password) => Assert.True(PasswordRules.IsStrong(password));

    // ---------------------------------------------------------------- the mobile number

    [Fact]
    public async Task AProfileSave_CannotChangeTheMobileNumber()
    {
        await using var api = await Api.StartAsync();
        var token = (await Json(await api.LoginAsync(UserName, Password))).GetProperty("accessToken").GetString()!;

        var change = await api.SendAsync(HttpMethod.Put, "/api/identity/auth/me/profile", token,
            new { displayName = UserName, phoneNumber = "09350000000" });
        var same = await api.SendAsync(HttpMethod.Put, "/api/identity/auth/me/profile", token,
            new { displayName = UserName, phoneNumber = UserPhone });

        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(UserPhone, await api.PhoneOfAsync(api.UserId));
    }

    [Fact]
    public async Task TheMobileNumber_ChangesOnlyWithThePasswordAndTheCodeSentToTheNewNumber()
    {
        await using var api = await Api.StartAsync();
        var session = await Json(await api.LoginAsync(UserName, Password));
        var token = session.GetProperty("accessToken").GetString()!;
        var otherSession = (await Json(await api.LoginAsync(UserName, Password))).GetProperty("refreshToken").GetString()!;

        // Wrong password: no SMS is sent.
        var wrongPassword = await api.SendAsync(HttpMethod.Post, "/api/identity/auth/me/phone/request", token,
            new { newPhoneNumber = "09350000000", currentPassword = "Wrong-Pass-1" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        Assert.Empty(api.Sms.Sent);

        var request = await api.SendAsync(HttpMethod.Post, "/api/identity/auth/me/phone/request", token,
            new { newPhoneNumber = "0935 000 0000", currentPassword = Password });
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var sms = Assert.Single(api.Sms.Sent);
        Assert.Equal("09350000000", sms.Phone);          // the code goes to the NEW number
        Assert.Equal(UserPhone, await api.PhoneOfAsync(api.UserId));   // nothing changed yet
        var code = Regex.Match(sms.Text, "[0-9]{6}").Value;

        var wrongCode = await api.SendAsync(HttpMethod.Post, "/api/identity/auth/me/phone/confirm", token,
            new { newPhoneNumber = "09350000000", code = "000000", currentPassword = Password });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCode.StatusCode);
        Assert.Equal(UserPhone, await api.PhoneOfAsync(api.UserId));

        // A code for one number does not confirm another.
        var otherNumber = await api.SendAsync(HttpMethod.Post, "/api/identity/auth/me/phone/confirm", token,
            new { newPhoneNumber = "09360000000", code, currentPassword = Password });
        Assert.Equal(HttpStatusCode.BadRequest, otherNumber.StatusCode);

        var confirm = await api.SendAsync(HttpMethod.Post, "/api/identity/auth/me/phone/confirm", token,
            new { newPhoneNumber = "09350000000", code, currentPassword = Password });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal("09350000000", await api.PhoneOfAsync(api.UserId));
        // The sign-ins started before the change are over.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.RefreshAsync(otherSession)).StatusCode);
        Assert.Contains("users.change_phone", await api.AuditActionsAsync());

        // The code is single use.
        var replay = await api.SendAsync(HttpMethod.Post, "/api/identity/auth/me/phone/confirm", token,
            new { newPhoneNumber = "09350000000", code, currentPassword = Password });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task ANumberAnotherUserHas_CannotBeClaimed()
    {
        await using var api = await Api.StartAsync();
        var token = (await Json(await api.LoginAsync(UserName, Password))).GetProperty("accessToken").GetString()!;

        var request = await api.SendAsync(HttpMethod.Post, "/api/identity/auth/me/phone/request", token,
            new { newPhoneNumber = "09120000001", currentPassword = Password });

        Assert.Equal(HttpStatusCode.Conflict, request.StatusCode);
        Assert.Empty(api.Sms.Sent);
    }

    // ---------------------------------------------------------------- users above the caller

    [Fact]
    public async Task AUserEditor_CannotChangeThePasswordOrMobileOrStatusOfSomeoneWhoHoldsMore()
    {
        await using var api = await Api.StartAsync();
        var managerToken = (await Json(await api.LoginAsync(Api.ManagerName, Password))).GetProperty("accessToken").GetString()!;

        var password = await api.SendAsync(HttpMethod.Put, $"/api/identity/users/{api.SeniorId}", managerToken,
            new { displayName = "Senior", isActive = true, password = "Takeover-Pass-77" });
        var phone = await api.SendAsync(HttpMethod.Put, $"/api/identity/users/{api.SeniorId}", managerToken,
            new { displayName = "Senior", isActive = true, phoneNumber = "09370000000" });
        var rename = await api.SendAsync(HttpMethod.Put, $"/api/identity/users/{api.SeniorId}", managerToken,
            new { displayName = "Senior Renamed", isActive = true });
        var peer = await api.SendAsync(HttpMethod.Put, $"/api/identity/users/{api.UserId}", managerToken,
            new { displayName = "Target User", isActive = true, password = "Peer-Reset-Pass-8" });

        Assert.Equal(HttpStatusCode.Forbidden, password.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, phone.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);   // other fields are still fine
        Assert.Equal(HttpStatusCode.OK, peer.StatusCode);     // someone who holds nothing the caller lacks
        Assert.Equal("09120000003", await api.PhoneOfAsync(api.SeniorId));
    }

    // ---------------------------------------------------------------- helpers

    private static string CookieValue(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("nexus_rt=", StringComparison.Ordinal));
        return header["nexus_rt=".Length..header.IndexOf(';')];
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private sealed class RecordingSms : ISmsSender
    {
        public ConcurrentQueue<(string Phone, string Text)> Queue { get; } = new();
        public IReadOnlyList<(string Phone, string Text)> Sent => Queue.ToList();

        public Task<Result<string>> SendAsync(Guid tenantId, string phoneNumber, string text, CancellationToken cancellationToken)
        {
            Queue.Enqueue((phoneNumber, text));
            return Task.FromResult(Result.Success("test-message-id"));
        }
    }

    private sealed class Api : IAsyncDisposable
    {
        public const string AdminName = "0000000001";
        public const string ManagerName = "0000000002";
        public const string SeniorName = "0000000003";

        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Api(WebApplication app, RecordingSms sms)
        {
            _app = app;
            _client = app.GetTestClient();
            Sms = sms;
        }

        public RecordingSms Sms { get; }
        public IServiceProvider Services => _app.Services;
        public Guid UserId { get; private set; }
        public Guid SeniorId { get; private set; }

        public static async Task<Api> StartAsync(int reuseGraceSeconds = 10, bool tokenInBody = true)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:LoginProtection:CaptchaAfterFailedAttempts"] = "100",
                ["Identity:LoginProtection:MaxFailedAttemptsPerIdentifier"] = "100",
                ["Identity:LoginProtection:ResetCodeCooldownSeconds"] = "0",
                ["Identity:LoginProtection:MaxResetCodesPerIdentifier"] = "50",
                ["Identity:Session:ReuseGraceSeconds"] = reuseGraceSeconds.ToString(),
                // Most tests drive the flow with the body token; the cookie-only default has its own test.
                ["Identity:Session:ReturnRefreshTokenInBody"] = tokenInBody.ToString(),
                ["Identity:Session:AcceptRefreshTokenInBody"] = tokenInBody.ToString(),
                ["PasswordReset:MinimumResponseMilliseconds"] = "0",
            });

            builder.Services.AddApplication();
            builder.Services.AddInfrastructure(builder.Configuration);
            var sms = new RecordingSms();
            builder.Services.RemoveAll<ISmsSender>();
            builder.Services.AddSingleton<ISmsSender>(sms);
            var database = Guid.NewGuid().ToString();
            builder.Services.RemoveAll<DbContextOptions<NexusCoreDbContext>>();
            builder.Services.AddDbContext<NexusCoreDbContext>((provider, options) => options
                .UseInMemoryDatabase(database)
                .AddInterceptors(provider.GetRequiredService<AuditingInterceptor>(), provider.GetRequiredService<DomainEventDispatchInterceptor>()));

            var jwt = new JwtOptions();
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = true,
                        ValidIssuer = jwt.Issuer,
                        ValidAudience = jwt.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                        ClockSkew = TimeSpan.FromMinutes(1)
                    };
                });
            builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddAuthorization(options =>
            {
                foreach (var permission in IdentityPermissions.All)
                {
                    options.AddPolicy(permission.Name, policy =>
                        policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
                }
            });

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapIdentityEndpoints();
            await app.StartAsync();

            var api = new Api(app, sms);
            await api.SeedAsync();
            return api;
        }

        private async Task SeedAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().HashPassword(Password);

            var tenant = new Tenant(Guid.NewGuid(), "Test", "test");
            db.Tenants.Add(tenant);
            var permissions = IdentityPermissions.All
                .Select(definition => new Permission(Guid.NewGuid(), definition.Name, definition.Module, definition.Description))
                .ToList();
            db.Permissions.AddRange(permissions);

            var adminRole = new Role(Guid.NewGuid(), tenant.Id, "Administrator", isSystem: true);
            adminRole.SetPermissions(permissions.Select(permission => permission.Id));
            // Can view and edit users - and nothing else.
            var managerRole = new Role(Guid.NewGuid(), tenant.Id, "User manager");
            managerRole.SetPermissions(permissions
                .Where(permission => permission.Name is IdentityPermissions.UsersView or IdentityPermissions.UsersUpdate)
                .Select(permission => permission.Id));
            db.Roles.AddRange(adminRole, managerRole);

            var admin = NewUser(tenant.Id, AdminName, "09120000001", hash);
            admin.MarkAsSystemAccount();
            admin.SetRoles([adminRole.Id]);
            var manager = NewUser(tenant.Id, ManagerName, "09120000002", hash);
            manager.SetRoles([managerRole.Id]);
            // Holds everything the manager does, and more (the whole administrator role).
            var senior = NewUser(tenant.Id, SeniorName, "09120000003", hash);
            senior.SetRoles([adminRole.Id]);
            var user = NewUser(tenant.Id, UserName, UserPhone, hash);
            db.Users.AddRange(admin, manager, senior, user);
            await db.SaveChangesAsync();

            UserId = user.Id;
            SeniorId = senior.Id;
        }

        private static User NewUser(Guid tenantId, string username, string phone, string hash)
        {
            var user = new User(Guid.NewGuid(), tenantId, null, username, hash);
            user.UpdateContactDetails(username, phone, notifySms: false);
            return user;
        }

        public Task<HttpResponseMessage> LoginAsync(string identifier, string password, bool cookieMode = false)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/auth/login") { Content = JsonContent.Create(new { identifier, password }) };
            if (cookieMode)
            {
                request.Headers.Add("X-Nexus-Session", "cookie");
            }

            return _client.SendAsync(request);
        }

        public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
            SendAsync(HttpMethod.Post, "/api/identity/auth/refresh", null, new { refreshToken });

        public Task<HttpResponseMessage> CookieRefreshAsync(string cookieValue, bool csrfHeader) =>
            CookieSendAsync(HttpMethod.Post, "/api/identity/auth/refresh", cookieValue, null, csrfHeader: csrfHeader);

        public Task<HttpResponseMessage> CookieSendAsync(HttpMethod method, string path, string cookieValue, object? body, bool csrfHeader = true, string? accessToken = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("Cookie", $"nexus_rt={cookieValue}");
            if (csrfHeader)
            {
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            }

            if (accessToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return _client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accessToken, object? body)
        {
            using var request = new HttpRequestMessage(method, path);
            if (accessToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return await _client.SendAsync(request);
        }

        public async Task<string?> PhoneOfAsync(Guid userId)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            return (await db.Users.AsNoTracking().SingleAsync(user => user.Id == userId)).PhoneNumber;
        }

        public async Task<IReadOnlyList<string>> AuditActionsAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            return await db.AuditLogs.AsNoTracking().Select(entry => entry.Action).ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
