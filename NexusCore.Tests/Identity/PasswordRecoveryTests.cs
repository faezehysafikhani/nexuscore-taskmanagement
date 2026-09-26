using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NexusCore.Application;
using NexusCore.Application.Common;
using NexusCore.Application.Endpoints;
using NexusCore.Application.Messaging;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.Domain.Settings;
using NexusCore.Infrastructure;
using NexusCore.Infrastructure.Persistence;
using NexusCore.Infrastructure.Security;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Tests.Identity;

/// <summary>
/// Password recovery by an SMS code, through the real endpoints, with an SMS adapter that
/// records messages instead of sending them.
/// </summary>
public sealed class PasswordRecoveryTests
{
    private const string OldPassword = "Old-Password-1";
    private const string NewPassword = "New-Password-2";
    private const string UserName = "0012345678";
    private const string UserPhone = "09121234567";
    private const string Answer = "در صورت وجود حساب کاربری معتبر، کد بازیابی به شماره تلفن ثبت‌شده ارسال خواهد شد.";

    // ---------------------------------------------------------------- step 1

    [Theory]
    [InlineData(UserName)]
    [InlineData(UserPhone)]
    [InlineData("+98 912 123 4567")]
    public async Task ACode_IsSentByUsernameOrPhone_ToTheAccountsMobile(string identifier)
    {
        await using var api = await Api.StartAsync();

        var response = await api.RequestCodeAsync(identifier);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Answer, (await Json(response)).GetProperty("message").GetString());
        var sms = Assert.Single(api.Sms.Sent);
        Assert.Equal(UserPhone, sms.Phone);
        Assert.Matches("[0-9]{6}", sms.Text);
    }

    [Theory]
    [InlineData("0099999999")]
    [InlineData("09350000000")]
    [InlineData("nobody")]
    public async Task AnUnknownAccount_GetsTheSameAnswer_AndNoSms(string identifier)
    {
        await using var api = await Api.StartAsync();
        var known = await (await api.RequestCodeAsync(UserName)).Content.ReadAsStringAsync();

        var response = await api.RequestCodeAsync(identifier);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(known, await response.Content.ReadAsStringAsync());
        Assert.Single(api.Sms.Sent);
    }

    [Fact]
    public async Task ADisabledAccount_GetsTheSameAnswer_AndCannotRecover()
    {
        await using var api = await Api.StartAsync();
        var code = await api.CodeForAsync(UserName);
        await api.SetActiveAsync(false);

        var again = await api.RequestCodeAsync(UserPhone);
        var verify = await api.VerifyAsync(UserName, code);

        Assert.Equal(Answer, (await Json(again)).GetProperty("message").GetString());
        Assert.Single(api.Sms.Sent);
        Assert.Equal("reset_code.invalid", await Code(verify));
    }

    [Fact]
    public async Task TheCodeIsStoredOnlyAsAHash()
    {
        await using var api = await Api.StartAsync();
        var code = await api.CodeForAsync(UserName);

        var stored = await api.ResetTokensAsync();

        var token = Assert.Single(stored);
        Assert.DoesNotContain(code, token.TokenHash);
        Assert.Equal(64, token.TokenHash.Length);
    }

    // ---------------------------------------------------------------- step 2

    [Fact]
    public async Task TheRightCode_GivesAResetToken_Once()
    {
        await using var api = await Api.StartAsync();
        var code = await api.CodeForAsync(UserName);

        var first = await api.VerifyAsync(UserName, code);
        var reuse = await api.VerifyAsync(UserName, code);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False(string.IsNullOrEmpty((await Json(first)).GetProperty("resetToken").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal("reset_code.invalid", await Code(reuse));
    }

    [Fact]
    public async Task PersianDigits_AreAccepted()
    {
        await using var api = await Api.StartAsync();
        var code = await api.CodeForAsync(UserName);
        var persian = string.Concat(code.Select(ch => (char)('۰' + (ch - '0'))));

        Assert.Equal(HttpStatusCode.OK, (await api.VerifyAsync(UserPhone, persian)).StatusCode);
    }

    [Fact]
    public async Task AWrongCode_IsRefused_InPersian()
    {
        await using var api = await Api.StartAsync();
        var code = await api.CodeForAsync(UserName);

        var response = await api.VerifyAsync(UserName, code == "000000" ? "111111" : "000000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal("reset_code.invalid", problem.GetProperty("title").GetString());
        Assert.Equal("کد تأیید صحیح نیست.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AnExpiredCode_IsSaidToBeExpired_OnlyWhenItIsTheRightOne()
    {
        await using var api = await Api.StartAsync();
        var code = await api.CodeForAsync(UserName);
        await api.ExpireCodesAsync();

        var right = await api.VerifyAsync(UserName, code);
        var wrong = await api.VerifyAsync(UserName, code == "000000" ? "111111" : "000000");

        Assert.Equal("reset_code.expired", await Code(right));
        Assert.Contains("اعتبار کد تأیید به پایان رسیده است", (await Json(right)).GetProperty("detail").GetString());
        Assert.Equal("reset_code.invalid", await Code(wrong));
    }

    [Fact]
    public async Task RequestingAgain_IsPaced_AndTheNewCodeReplacesTheOld()
    {
        await using var api = await Api.StartAsync(cooldownSeconds: 1, maxCodes: 2);
        var first = await api.CodeForAsync(UserName);

        var tooSoon = await api.RequestCodeAsync(UserName);
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        Assert.Single(api.Sms.Sent);

        await Task.Delay(1100);
        var second = await api.CodeForAsync(UserPhone);
        Assert.Equal(2, api.Sms.Sent.Count);

        if (first != second)
        {
            Assert.Equal("reset_code.invalid", await Code(await api.VerifyAsync(UserName, first)));
        }

        // The account had its two codes (by username, then by mobile number): a third is not sent,
        // but the answer does not show it.
        await Task.Delay(1100);
        var third = await api.RequestCodeAsync(UserName);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.Equal(Answer, (await Json(third)).GetProperty("message").GetString());
        Assert.Equal(2, api.Sms.Sent.Count);

        // And the identifier as typed is over its own limit now.
        await Task.Delay(1100);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await api.RequestCodeAsync(UserName)).StatusCode);

        // The same limits for an identifier nobody has: nothing to learn from them.
        Assert.Equal(HttpStatusCode.OK, (await api.RequestCodeAsync("0099999999")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await api.RequestCodeAsync("0099999999")).StatusCode);
    }

    [Fact]
    public async Task TooManyWrongCodes_LockTheIdentifier_AndWithdrawTheCode()
    {
        await using var api = await Api.StartAsync();
        var code = await api.CodeForAsync(UserName);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("reset_code.invalid", await Code(await api.VerifyAsync(UserName, wrong)));
        }

        var right = await api.VerifyAsync(UserName, code);
        Assert.Equal(HttpStatusCode.TooManyRequests, right.StatusCode);
        Assert.All(await api.ResetTokensAsync(), token => Assert.NotNull(token.InvalidatedAtUtc));
    }

    // ---------------------------------------------------------------- step 3

    [Fact]
    public async Task TheNewPassword_Works_TheOldOneDoesNot_AndSessionsEnd()
    {
        await using var api = await Api.StartAsync();
        var session = await Json(await api.LoginAsync(UserName, OldPassword));
        var refreshToken = session.GetProperty("refreshToken").GetString()!;
        var code = await api.CodeForAsync(UserName);
        var resetToken = (await Json(await api.VerifyAsync(UserName, code))).GetProperty("resetToken").GetString()!;

        var reset = await api.PostAsync("/api/identity/auth/reset-password", new { token = resetToken, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.LoginAsync(UserName, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.LoginAsync(UserPhone, OldPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.PostAsync("/api/identity/auth/refresh", new { refreshToken })).StatusCode);

        // The reset token is spent.
        var again = await api.PostAsync("/api/identity/auth/reset-password", new { token = resetToken, newPassword = "Another-Password-3" });
        Assert.Equal("reset_token.invalid", await Code(again));
        Assert.DoesNotContain(NewPassword, await reset.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheNewPassword_FollowsThePasswordPolicy_InPersian()
    {
        await using var api = await Api.StartAsync();
        var code = await api.CodeForAsync(UserName);
        var resetToken = (await Json(await api.VerifyAsync(UserName, code))).GetProperty("resetToken").GetString()!;

        var tooShort = await api.PostAsync("/api/identity/auth/reset-password", new { token = resetToken, newPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Contains("حداقل ۸ کاراکتر", (await Json(tooShort)).GetProperty("detail").GetString());
    }

    // ---------------------------------------------------------------- unexpected errors

    [Fact]
    public async Task AnUnexpectedException_IsAnsweredWithoutInternals_EvenInDevelopment()
    {
        await using var api = await Api.StartAsync(environment: "Development");

        var response = await api.GetAsync("/test/boom");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("server.error", (await Json(response)).GetProperty("title").GetString());
        Assert.Contains("خطایی در انجام عملیات رخ داد", body);
        foreach (var secret in new[] { "SELECT", "identity.Users", "InvalidOperationException", "C:\\secret", "   at " })
        {
            Assert.DoesNotContain(secret, body);
        }
    }

    [Fact]
    public async Task AMalformedRequest_IsAnsweredInPersian()
    {
        await using var api = await Api.StartAsync(environment: "Development");

        var response = await api.RawPostAsync("/api/identity/auth/forgot-password", "{ not json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal("validation.error", problem.GetProperty("title").GetString());
        Assert.Matches("[\u0600-\u06FF]", problem.GetProperty("detail").GetString()!);
    }

    // ---------------------------------------------------------------- host

    // ---------------------------------------------------------------- the SMS text

    [Fact]
    public async Task TheSms_IsTheResetTemplate_WithTheCodeAndItsLifetime_AndNoPassword()
    {
        await using var api = await Api.StartAsync(settings: new() { ["PasswordReset:CodeLifetimeMinutes"] = "10" });

        var before = DateTimeOffset.UtcNow;
        var code = await api.CodeForAsync(UserName);

        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal($"کد بازیابی رمز عبور شما: {code}\nاین کد تا 10 دقیقه معتبر است.", api.Sms.Sent[^1].Text);
        Assert.DoesNotContain(OldPassword, api.Sms.Sent[^1].Text);
        var token = Assert.Single(await api.ResetTokensAsync());
        Assert.InRange(token.ExpiresAtUtc, before.AddMinutes(10).AddSeconds(-5), DateTimeOffset.UtcNow.AddMinutes(10).AddSeconds(5));
        Assert.Equal(HttpStatusCode.OK, (await api.VerifyAsync(UserName, code)).StatusCode);
    }

    [Fact]
    public async Task TheOrganizationsEditedText_IsUsed_WithTheConfiguredProductName()
    {
        await using var api = await Api.StartAsync(settings: new() { ["Notifications:SmsTemplates:ProductName"] = "سامانه آزمایشی" });
        await api.StoreResetTemplateAsync("{ProductName}\nکد: {Code} (تا {ExpireMinutes} دقیقه)");

        var code = await api.CodeForAsync(UserName);

        Assert.Equal($"سامانه آزمایشی\nکد: {code} (تا 5 دقیقه)", api.Sms.Sent[^1].Text);
        Assert.Equal(HttpStatusCode.OK, (await api.VerifyAsync(UserName, code)).StatusCode);
    }

    [Theory]
    [InlineData("رمز شما به زودی ارسال می‌شود")]
    [InlineData("   ")]
    public async Task AStoredTextWithoutTheCode_FallsBackToTheDefault_AndRecoveryStillWorks(string broken)
    {
        await using var api = await Api.StartAsync();
        await api.StoreResetTemplateAsync(broken);

        var code = await api.CodeForAsync(UserName);

        Assert.Equal($"کد بازیابی رمز عبور شما: {code}\nاین کد تا 5 دقیقه معتبر است.", api.Sms.Sent[^1].Text);
        Assert.Equal(HttpStatusCode.OK, (await api.VerifyAsync(UserName, code)).StatusCode);
    }

    [Fact]
    public async Task AnAccountWithoutAMobile_GetsTheSameAnswer_AndNoSms()
    {
        await using var api = await Api.StartAsync();
        await api.ClearPhoneAsync();

        var response = await api.RequestCodeAsync(UserName);

        Assert.Equal(Answer, (await Json(response)).GetProperty("message").GetString());
        Assert.Empty(api.Sms.Sent);
        Assert.Empty(await api.ResetTokensAsync());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> Code(HttpResponseMessage response) =>
        (await Json(response)).GetProperty("title").GetString();

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
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Api(WebApplication app, RecordingSms sms)
        {
            _app = app;
            _client = app.GetTestClient();
            Sms = sms;
        }

        public RecordingSms Sms { get; }

        public static async Task<Api> StartAsync(int cooldownSeconds = 60, int maxCodes = 3, string environment = "Testing", Dictionary<string, string?>? settings = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:LoginProtection:CaptchaAfterFailedAttempts"] = "100",
                ["Identity:LoginProtection:ResetCodeCooldownSeconds"] = cooldownSeconds.ToString(),
                ["Identity:LoginProtection:MaxResetCodesPerIdentifier"] = maxCodes.ToString(),
            });
            if (settings is not null)
            {
                builder.Configuration.AddInMemoryCollection(settings);
            }

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
                        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    };
                });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseSafeErrorResponses();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapIdentityEndpoints();
            app.MapGet("/test/boom", (HttpContext _) =>
            {
                throw new InvalidOperationException(@"SELECT * FROM identity.Users failed at C:\secret\app.dll");
            });
            await app.StartAsync();

            var api = new Api(app, sms);
            await api.SeedAsync();
            return api;
        }

        private async Task SeedAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            var tenant = new Tenant(Guid.NewGuid(), "Test", "test");
            db.Tenants.Add(tenant);
            var user = new User(Guid.NewGuid(), tenant.Id, null, "Recovering User",
                scope.ServiceProvider.GetRequiredService<IPasswordHasher>().HashPassword(OldPassword));
            user.UpdateContactDetails(UserName, UserPhone, notifySms: false);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        public Task<HttpResponseMessage> RequestCodeAsync(string identifier) =>
            PostAsync("/api/identity/auth/forgot-password", new { identifier });

        public Task<HttpResponseMessage> VerifyAsync(string identifier, string code) =>
            PostAsync("/api/identity/auth/forgot-password/verify", new { identifier, code });

        public Task<HttpResponseMessage> LoginAsync(string identifier, string password) =>
            PostAsync("/api/identity/auth/login", new { identifier, password });

        /// <summary>Requests a code and reads it from the SMS that was "sent".</summary>
        public async Task<string> CodeForAsync(string identifier)
        {
            var before = Sms.Sent.Count;
            Assert.Equal(HttpStatusCode.OK, (await RequestCodeAsync(identifier)).StatusCode);
            Assert.Equal(before + 1, Sms.Sent.Count);
            return Regex.Match(Sms.Sent[^1].Text, "[0-9]{6}").Value;
        }

        public async Task SetActiveAsync(bool isActive)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            (await db.Users.SingleAsync()).SetActive(isActive);
            await db.SaveChangesAsync();
        }

        /// <summary>The organization's own password-reset text, as the SMS panel stores it.</summary>
        public async Task StoreResetTemplateAsync(string text)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            var user = await db.Users.SingleAsync();
            db.Settings.Add(new SystemSetting(Guid.NewGuid(), user.TenantId, "Notifications.SmsTemplates." + SmsTemplateKeys.PasswordReset, text, "Integrations"));
            await db.SaveChangesAsync();
        }

        public async Task ClearPhoneAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            var user = await db.Users.SingleAsync();
            user.UpdateContactDetails(user.Username, null, notifySms: false);
            await db.SaveChangesAsync();
        }

        public async Task ExpireCodesAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            foreach (var token in await db.Set<PasswordResetToken>().ToListAsync())
            {
                db.Entry(token).Property(t => t.ExpiresAtUtc).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
            }

            await db.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<PasswordResetToken>> ResetTokensAsync()
        {
            using var scope = _app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>().Set<PasswordResetToken>().AsNoTracking().ToListAsync();
        }

        public Task<HttpResponseMessage> PostAsync(string path, object body) => _client.PostAsync(path, JsonContent.Create(body));

        public Task<HttpResponseMessage> RawPostAsync(string path, string body) =>
            _client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

        public Task<HttpResponseMessage> GetAsync(string path) => _client.GetAsync(path);

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
