using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NexusCore.Application.Security.RateLimiting;

public static class NexusRateLimitingExtensions
{
    public static IServiceCollection AddNexusCoreRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configured =
            configuration.GetSection(NexusRateLimitingOptions.SectionName).Get<NexusRateLimitingOptions>()
            ?? new NexusRateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                    title: "too_many_requests",
                    detail: "تعداد درخواست‌ها بیش از حد مجاز است. لطفاً کمی بعد دوباره تلاش کنید.",
                    statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(context.HttpContext);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                CreatePartition(ClientPartition(http, "global"), configured.Enabled, configured.Global));

            options.AddPolicy(NexusRateLimitPolicies.Auth, http =>
                CreatePartition(ClientPartition(http, "auth"), configured.Enabled, configured.Auth));

            options.AddPolicy(NexusRateLimitPolicies.PasswordRecovery, http =>
                CreatePartition(ClientPartition(http, "password-recovery"), configured.Enabled, configured.PasswordRecovery));

            options.AddPolicy(NexusRateLimitPolicies.PasswordResetVerification, http =>
                CreatePartition(ClientPartition(http, "password-reset-verification"), configured.Enabled, configured.PasswordResetVerification));

            options.AddPolicy(NexusRateLimitPolicies.AuthenticatedApi, http =>
                CreatePartition(UserOrClientPartition(http, "authenticated-api"), configured.Enabled, configured.AuthenticatedApi));

            options.AddPolicy(NexusRateLimitPolicies.Write, http =>
                CreatePartition(UserOrClientPartition(http, "write"), configured.Enabled, configured.Write));

            options.AddPolicy(NexusRateLimitPolicies.Upload, http =>
                CreatePartition(UserOrClientPartition(http, "upload"), configured.Enabled, configured.Upload));

            options.AddPolicy(NexusRateLimitPolicies.ChatSend, http =>
                CreatePartition(UserOrClientPartition(http, "chat-send"), configured.Enabled, configured.ChatSend));

            options.AddPolicy(NexusRateLimitPolicies.Sms, http =>
                CreatePartition(UserOrClientPartition(http, "sms"), configured.Enabled, configured.Sms));

            options.AddPolicy(NexusRateLimitPolicies.Realtime, http =>
                CreatePartition(UserOrClientPartition(http, "realtime"), configured.Enabled, configured.Realtime));
        });

        return services;
    }

    public static IApplicationBuilder UseNexusCoreRateLimiting(this IApplicationBuilder app) =>
        app.UseRateLimiter();

    private static RateLimitPartition<string> CreatePartition(
        string partitionKey,
        bool enabled,
        RateLimitRule rule)
    {
        if (!enabled)
        {
            return RateLimitPartition.GetNoLimiter(partitionKey);
        }

        var permitLimit = Math.Max(1, rule.PermitLimit);
        var window = TimeSpan.FromMinutes(Math.Max(1, rule.WindowMinutes));

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    private static string ClientPartition(HttpContext http, string policy) =>
        $"{policy}:{ClientAddress(http)}";

    private static string UserOrClientPartition(HttpContext http, string policy)
    {
        var userId =
            http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub")
            ?? http.User.FindFirstValue("userId");

        return string.IsNullOrWhiteSpace(userId)
            ? ClientPartition(http, policy)
            : $"{policy}:user:{userId}";
    }

    private static string ClientAddress(HttpContext http)
    {
        var forwardedFor = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var first = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }

        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
