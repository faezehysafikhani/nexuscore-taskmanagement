using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure;
using NexusCore.Infrastructure.Persistence;
using NexusCore.Infrastructure.Security;

namespace NexusCore.Tests.Identity;

/// <summary>
/// Sign-in, refresh, sign-out and disabled accounts through the real identity endpoints, JWT
/// bearer authentication and permission policies, on an in-memory database.
/// </summary>
public sealed class AccountStatusTests
{
    private const string Password = "Correct-Pass-1";
    private const string UserName = "0012345678";
    private const string UserPhone = "09121234567";

    // ---------------------------------------------------------------- sign-in names

    [Fact]
    public async Task Login_WithUsernameAndPassword_Succeeds()
    {
        await using var api = await Api.StartAsync();

        var response = await api.LoginAsync(UserName, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((await Json(response)).GetProperty("accessToken").GetString()));
    }

    [Theory]
    [InlineData(UserPhone)]
    [InlineData("+98 912 123 4567")]
    [InlineData("۰۹۱۲۱۲۳۴۵۶۷")]
    public async Task Login_WithPhoneNumberAndPassword_Succeeds(string phone)
    {
        await using var api = await Api.StartAsync();

        var response = await api.LoginAsync(phone, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Wrong username, wrong mobile number, wrong password, an email address and a disabled
    // account with a wrong password all get exactly the same answer.
    [Theory]
    [InlineData("0099999999", Password, false)]
    [InlineData("09350000000", Password, false)]
    [InlineData(UserName, "Wrong-Pass-1", false)]
    [InlineData(UserPhone, "Wrong-Pass-1", false)]
    [InlineData("user@example.com", Password, false)]
    [InlineData(UserName, "Wrong-Pass-1", true)]
    public async Task Login_Failures_AllLookTheSame(string identifier, string password, bool disableFirst)
    {
        await using var api = await Api.StartAsync();
        if (disableFirst)
        {
            await api.SetActiveDirectlyAsync(api.UserId, false);
        }

        var response = await api.LoginAsync(identifier, password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal("unauthorized", problem.GetProperty("title").GetString());
        Assert.Equal("Invalid username/mobile number or password.", problem.GetProperty("detail").GetString());
    }

    // ---------------------------------------------------------------- disabled accounts

    [Fact]
    public async Task Login_DisabledAccountWithTheRightPassword_IsToldTheAccountIsDisabled()
    {
        await using var api = await Api.StartAsync();
        await api.SetActiveDirectlyAsync(api.UserId, false);

        var response = await api.LoginAsync(UserName, Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account.disabled", (await Json(response)).GetProperty("title").GetString());
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disabling_EndsTheSession_AccessTokenAndRefreshToken()
    {
        await using var api = await Api.StartAsync();
        var session = await Json(await api.LoginAsync(UserName, Password));
        var accessToken = session.GetProperty("accessToken").GetString()!;
        var refreshToken = session.GetProperty("refreshToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await api.MeAsync(accessToken)).StatusCode);

        var disable = await api.SetStatusAsync(api.AdminToken, api.UserId, false);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        // The access token is still signed and unexpired, but no longer accepted.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.MeAsync(accessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.RefreshAsync(refreshToken)).StatusCode);
        Assert.False(await api.IsActiveInDatabaseAsync(api.UserId));
        Assert.Contains("users.disable", await api.AuditActionsAsync());

        // Other users are not affected.
        Assert.Equal(HttpStatusCode.OK, (await api.MeAsync(api.AdminToken)).StatusCode);
        Assert.True(await api.IsActiveInDatabaseAsync(api.AdminId));
    }

    [Fact]
    public async Task Refresh_ForADisabledAccount_IsRefusedEvenIfTheTokenWasNotRevoked()
    {
        await using var api = await Api.StartAsync();
        var refreshToken = (await Json(await api.LoginAsync(UserName, Password))).GetProperty("refreshToken").GetString()!;
        // Disabled straight in the database, so the refresh token itself is still active.
        await api.SetActiveDirectlyAsync(api.UserId, false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.RefreshAsync(refreshToken)).StatusCode);
    }

    [Fact]
    public async Task Reenabling_LetsTheUserSignInAgain()
    {
        await using var api = await Api.StartAsync();
        await api.SetStatusAsync(api.AdminToken, api.UserId, false);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.LoginAsync(UserName, Password)).StatusCode);

        var enable = await api.SetStatusAsync(api.AdminToken, api.UserId, true);

        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var session = await Json(await api.LoginAsync(UserName, Password));
        Assert.Equal(HttpStatusCode.OK, (await api.MeAsync(session.GetProperty("accessToken").GetString()!)).StatusCode);
        Assert.Contains("users.enable", await api.AuditActionsAsync());
    }

    [Fact]
    public async Task WithoutChangeStatusPermission_AUserCannotDisableAnother_NeitherDirectlyNorThroughAnEdit()
    {
        await using var api = await Api.StartAsync();
        var managerToken = (await Json(await api.LoginAsync(Api.ManagerName, Password))).GetProperty("accessToken").GetString()!;

        var patch = await api.SetStatusAsync(managerToken, api.UserId, false);
        var edit = await api.SendAsync(HttpMethod.Put, $"/api/identity/users/{api.UserId}", managerToken,
            new { displayName = "Target User", isActive = false });

        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.True(await api.IsActiveInDatabaseAsync(api.UserId));
    }

    [Fact]
    public async Task TheSystemAdministratorAndYourOwnAccount_CannotBeDisabled()
    {
        await using var api = await Api.StartAsync();

        var system = await api.SetStatusAsync(api.AdminToken, api.AdminId, false);

        Assert.Equal(HttpStatusCode.Forbidden, system.StatusCode);
        Assert.True(await api.IsActiveInDatabaseAsync(api.AdminId));
    }

    // ---------------------------------------------------------------- sign-out, registration

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        await using var api = await Api.StartAsync();
        var refreshToken = (await Json(await api.LoginAsync(UserName, Password))).GetProperty("refreshToken").GetString()!;

        var logout = await api.SendAsync(HttpMethod.Post, "/api/identity/auth/logout", null, new { refreshToken });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.RefreshAsync(refreshToken)).StatusCode);
        Assert.Contains("identity.logout", await api.AuditActionsAsync());

        // An unknown or already used token gets the same answer.
        var again = await api.SendAsync(HttpMethod.Post, "/api/identity/auth/logout", null, new { refreshToken });
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task ThereIsNoSelfRegistration()
    {
        await using var api = await Api.StartAsync();

        var response = await api.SendAsync(HttpMethod.Post, "/api/identity/auth/register", null,
            new { username = "0055555555", password = Password, displayName = "New" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- attempt limits

    [Fact]
    public async Task DisabledAccount_DoesNotBypassTheCaptcha()
    {
        // Default protection: a CAPTCHA is due after one failed attempt.
        await using var api = await Api.StartAsync(captchaAfterFailedAttempts: 1);
        await api.SetActiveDirectlyAsync(api.UserId, false);

        var first = await api.LoginAsync(UserName, "Wrong-Pass-1");
        var second = await api.LoginAsync(UserName, Password);

        Assert.Equal("unauthorized.captcha_required", (await Json(first)).GetProperty("title").GetString());
        // The right password does not help without the CAPTCHA, and reveals nothing yet.
        Assert.Equal("captcha.required", (await Json(second)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------- host

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private sealed class Api : IAsyncDisposable
    {
        public const string AdminName = "0000000001";
        public const string ManagerName = "0000000002";

        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Api(WebApplication app)
        {
            _app = app;
            _client = app.GetTestClient();
        }

        public Guid AdminId { get; private set; }
        public Guid UserId { get; private set; }
        public string AdminToken { get; private set; } = string.Empty;

        public static async Task<Api> StartAsync(int captchaAfterFailedAttempts = 100)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:LoginProtection:CaptchaAfterFailedAttempts"] = captchaAfterFailedAttempts.ToString(),
            });

            builder.Services.AddApplication();
            builder.Services.AddInfrastructure(builder.Configuration);
            var database = Guid.NewGuid().ToString();
            builder.Services.RemoveAll<DbContextOptions<NexusCoreDbContext>>();
            builder.Services.AddDbContext<NexusCoreDbContext>((provider, options) => options
                .UseInMemoryDatabase(database)
                .AddInterceptors(provider.GetRequiredService<AuditingInterceptor>(), provider.GetRequiredService<DomainEventDispatchInterceptor>()));

            // The same bearer and policy set-up as the API hosts (NexusCore.Api, Rozet.Api).
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
                    options.Events = new JwtBearerEvents();
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

            var api = new Api(app);
            await api.SeedAsync();
            api.AdminToken = (await Json(await api.LoginAsync(AdminName, Password))).GetProperty("accessToken").GetString()!;
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
            // Can view and edit users, but not enable or disable them.
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
            var user = NewUser(tenant.Id, UserName, UserPhone, hash);
            db.Users.AddRange(admin, manager, user);
            await db.SaveChangesAsync();

            AdminId = admin.Id;
            UserId = user.Id;
        }

        private static User NewUser(Guid tenantId, string username, string phone, string hash)
        {
            var user = new User(Guid.NewGuid(), tenantId, null, username, hash);
            user.UpdateContactDetails(username, phone, notifySms: false);
            return user;
        }

        public Task<HttpResponseMessage> LoginAsync(string identifier, string password) =>
            SendAsync(HttpMethod.Post, "/api/identity/auth/login", null, new { identifier, password });

        public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
            SendAsync(HttpMethod.Post, "/api/identity/auth/refresh", null, new { refreshToken });

        public Task<HttpResponseMessage> MeAsync(string accessToken) =>
            SendAsync(HttpMethod.Get, "/api/identity/auth/me", accessToken, null);

        public Task<HttpResponseMessage> SetStatusAsync(string accessToken, Guid userId, bool isActive) =>
            SendAsync(HttpMethod.Patch, $"/api/identity/users/{userId}/status", accessToken, new { isActive });

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

        public async Task SetActiveDirectlyAsync(Guid userId, bool isActive)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            (await db.Users.SingleAsync(user => user.Id == userId)).SetActive(isActive);
            await db.SaveChangesAsync();
        }

        public async Task<bool> IsActiveInDatabaseAsync(Guid userId)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            return (await db.Users.AsNoTracking().SingleAsync(user => user.Id == userId)).IsActive;
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
