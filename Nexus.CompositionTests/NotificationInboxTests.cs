using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure;
using NexusCore.Infrastructure.Security;
using NexusCore.SharedKernel.Interfaces;
using Notifications.Api.Endpoints;
using Notifications.Application;
using Notifications.Application.Abstractions;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Persistence;

namespace Nexus.CompositionTests;

/// <summary>The notification endpoints the bell reads: the caller's own notifications only, in their own tenant.</summary>
public sealed class NotificationInboxTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "unused" });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddNotificationApplication();
        builder.Services.AddNotificationInfrastructure(builder.Configuration);
        var database = Guid.NewGuid().ToString();
        builder.Services.RemoveAll<DbContextOptions<NotificationDbContext>>();
        builder.Services.AddDbContext<NotificationDbContext>(options => options.UseInMemoryDatabase(database));

        var jwt = new JwtOptions();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            };
        });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapNotificationEndpoints();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task EachUser_SeesOnlyTheirOwnNotifications_AndCannotMarkSomeoneElses()
    {
        await StoreAsync(_alice, _tenant, "یادآوری وظیفه: گزارش هفتگی");
        await StoreAsync(_bob, _tenant, "یادآوری وظیفه: جلسه");

        var alices = await ListAsync(_alice, _tenant);
        var bobs = await ListAsync(_bob, _tenant);

        Assert.Equal("یادآوری وظیفه: گزارش هفتگی", Assert.Single(alices).GetProperty("title").GetString());
        Assert.Equal("یادآوری وظیفه: جلسه", Assert.Single(bobs).GetProperty("title").GetString());

        var aliceNotification = alices[0].GetProperty("id").GetGuid();
        var byBob = await SendAsync(_bob, _tenant, HttpMethod.Put, $"/api/notifications/{aliceNotification}/read");
        Assert.Equal(HttpStatusCode.NotFound, byBob.StatusCode);
        Assert.False((await ListAsync(_alice, _tenant))[0].GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task ReadAndUnread_AreKeptOnTheServer()
    {
        await StoreAsync(_alice, _tenant, "اول");
        await StoreAsync(_alice, _tenant, "دوم");
        await StoreAsync(_alice, _tenant, "سوم");
        Assert.Equal(3, await UnreadAsync(_alice, _tenant));

        var first = (await ListAsync(_alice, _tenant))[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_alice, _tenant, HttpMethod.Put, $"/api/notifications/{first}/read")).StatusCode);
        Assert.Equal(2, await UnreadAsync(_alice, _tenant));

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_alice, _tenant, HttpMethod.Put, "/api/notifications/read-all")).StatusCode);
        Assert.Equal(0, await UnreadAsync(_alice, _tenant));
        Assert.All(await ListAsync(_alice, _tenant), n => Assert.True(n.GetProperty("isRead").GetBoolean()));
    }

    [Fact]
    public async Task ANotificationOfAnotherTenant_IsNotShown_OrCounted()
    {
        await StoreAsync(_alice, _tenant, "همین سازمان");
        await StoreAsync(_alice, _otherTenant, "سازمان دیگر");
        await StoreAsync(_alice, null, "قدیمی بدون سازمان");

        var titles = (await ListAsync(_alice, _tenant)).Select(n => n.GetProperty("title").GetString()).ToList();

        Assert.Contains("همین سازمان", titles);
        Assert.Contains("قدیمی بدون سازمان", titles);
        Assert.DoesNotContain("سازمان دیگر", titles);
        Assert.Equal(2, await UnreadAsync(_alice, _tenant));
    }

    [Fact]
    public async Task TheListIsPaged()
    {
        for (var i = 0; i < 5; i++)
        {
            await StoreAsync(_alice, _tenant, $"اعلان {i}");
        }

        Assert.Equal(2, (await ListAsync(_alice, _tenant, "?pageSize=2")).Count);
        Assert.Single(await ListAsync(_alice, _tenant, "?pageSize=2&pageNumber=3"));
    }

    [Fact]
    public async Task WithoutSignIn_NothingIsReturned()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/notifications")).StatusCode);
    }

    private async Task StoreAsync(Guid userId, Guid? tenantId, string title)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(userId, title, "متن", "Warning", default, tenantId);
        await Task.Delay(5); // distinct creation times, for a stable order
    }

    private async Task<List<JsonElement>> ListAsync(Guid userId, Guid tenantId, string query = "")
    {
        var response = await SendAsync(userId, tenantId, HttpMethod.Get, "/api/notifications" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToList();
    }

    private async Task<int> UnreadAsync(Guid userId, Guid tenantId)
    {
        var response = await SendAsync(userId, tenantId, HttpMethod.Get, "/api/notifications/unread-count");
        return int.Parse(await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> SendAsync(Guid userId, Guid tenantId, HttpMethod method, string path)
    {
        var token = new JwtTokenService(Options.Create(new JwtOptions()))
            .CreateAccessToken(new User(userId, tenantId, null, "User", "not-used"), []).Token;
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }
}
