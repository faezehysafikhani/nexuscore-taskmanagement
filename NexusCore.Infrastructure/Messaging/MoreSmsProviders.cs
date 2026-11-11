using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Messaging;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Messaging;

/// <summary>
/// What the gateways have in common: one HTTP call through the module's guarded client (no redirects, address checked when
/// connecting), a JSON answer with a success flag, and failures that never carry the key or the URL.
/// </summary>
public abstract class JsonSmsProvider(IHttpClientFactory httpClientFactory, ILogger logger) : ISmsProvider
{
    public abstract string Key { get; }
    public abstract string DisplayName { get; }
    public abstract string DefaultBaseUrl { get; }

    protected static readonly Error NetworkFailure = Error.Validation("ارتباط با سرویس پیامک برقرار نشد. آدرس سرویس و اتصال شبکه سرور را بررسی کنید.");

    protected HttpClient Client => httpClientFactory.CreateClient(KavenegarSmsProvider.HttpClientName);

    protected static string BaseOf(SmsProviderSettings settings, string fallback) =>
        (string.IsNullOrWhiteSpace(settings.BaseUrl) ? fallback : settings.BaseUrl).TrimEnd('/');

    public abstract Task<Result<string>> SendAsync(SmsProviderSettings settings, string phoneNumber, string text, CancellationToken cancellationToken);

    protected async Task<Result<string>> CallAsync(Func<HttpClient, Task<HttpResponseMessage>> send, Func<JsonElement, int, Result<string>> interpret)
    {
        try
        {
            using var response = await send(Client);
            var body = await response.Content.ReadAsStringAsync();
            JsonDocument? json = null;
            try
            {
                json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            }
            catch (JsonException)
            {
                // Not the gateway's JSON: a wrong address, a proxy page, ...
            }

            using (json)
            {
                if (json is null || json.RootElement.ValueKind != JsonValueKind.Object)
                {
                    logger.LogWarning("{Provider} answered HTTP {Status} without JSON.", Key, (int)response.StatusCode);
                    return Result.Failure<string>(Error.Validation($"پاسخ نامعتبر از سرویس پیامک (HTTP {(int)response.StatusCode}). آدرس سرویس را بررسی کنید."));
                }

                return interpret(json.RootElement, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            // The exception text can hold the request URL and with it the key.
            logger.LogWarning("{Provider} request failed: {ErrorType}.", Key, ex.GetType().Name);
            return Result.Failure<string>(NetworkFailure);
        }
    }

    protected static Result<string> Refused(string provider, string? detail) =>
        Result.Failure<string>(Error.Validation($"خطای {provider}: {(string.IsNullOrWhiteSpace(detail) ? "ارسال پیامک ناموفق بود." : detail)}"));
}

/// <summary>Melipayamak (ملی‌پیامک) REST: POST {base}/SendSMS/SendSMS with username, password, to, from and text; RetStatus 1 is success.</summary>
public sealed class MelipayamakSmsProvider(IHttpClientFactory httpClientFactory, ILogger<MelipayamakSmsProvider> logger) : JsonSmsProvider(httpClientFactory, logger)
{
    public override string Key => "melipayamak";
    public override string DisplayName => "ملی‌پیامک (Melipayamak)";
    public override string DefaultBaseUrl => "https://rest.payamak-panel.com/api";

    public override async Task<Result<string>> SendAsync(SmsProviderSettings settings, string phoneNumber, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password))
        {
            return Result.Failure<string>(Error.Validation("ملی‌پیامک به نام کاربری و رمز عبور پنل نیاز دارد."));
        }

        var payload = new { username = settings.Username, password = settings.Password, to = phoneNumber, from = settings.SenderNumber ?? string.Empty, text, isFlash = false };
        return await CallAsync(
            http => http.PostAsJsonAsync($"{BaseOf(settings, DefaultBaseUrl)}/SendSMS/SendSMS", payload, cancellationToken),
            (json, _) =>
            {
                var status = json.TryGetProperty("RetStatus", out var s) && s.TryGetInt32(out var code) ? code : 0;
                if (status == 1)
                {
                    return Result.Success(json.TryGetProperty("Value", out var v) ? v.ToString() : "accepted");
                }

                return Refused("ملی‌پیامک", json.TryGetProperty("StrRetStatus", out var m) ? m.GetString() : null);
            });
    }
}

/// <summary>Ghasedak (قاصدک) v2: POST {base}/v2/sms/send/simple with the key in the apikey header and receptor, message, linenumber; result.code 200 is success.</summary>
public sealed class GhasedakSmsProvider(IHttpClientFactory httpClientFactory, ILogger<GhasedakSmsProvider> logger) : JsonSmsProvider(httpClientFactory, logger)
{
    public override string Key => "ghasedak";
    public override string DisplayName => "قاصدک (Ghasedak)";
    public override string DefaultBaseUrl => "https://api.ghasedak.me";

    public override async Task<Result<string>> SendAsync(SmsProviderSettings settings, string phoneNumber, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return Result.Failure<string>(Error.Validation("قاصدک برای ارسال پیامک به کلید API نیاز دارد."));
        }

        var form = new Dictionary<string, string> { ["receptor"] = phoneNumber, ["message"] = text };
        if (!string.IsNullOrWhiteSpace(settings.SenderNumber))
        {
            form["linenumber"] = settings.SenderNumber.Trim();
        }

        return await CallAsync(
            async http =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseOf(settings, DefaultBaseUrl)}/v2/sms/send/simple") { Content = new FormUrlEncodedContent(form) };
                request.Headers.TryAddWithoutValidation("apikey", settings.ApiKey);
                return await http.SendAsync(request, cancellationToken);
            },
            (json, http) =>
            {
                var result = json.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Object ? r : default;
                var code = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : http;
                if (code == 200)
                {
                    return Result.Success(json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0 ? items[0].ToString() : "accepted");
                }

                return Refused("قاصدک", result.ValueKind == JsonValueKind.Object && result.TryGetProperty("message", out var m) ? m.GetString() : null);
            });
    }
}

/// <summary>SMS.ir v1: POST {base}/v1/send/bulk with the key in X-API-KEY and lineNumber, messageText, mobiles; status 1 is success.</summary>
public sealed class SmsIrSmsProvider(IHttpClientFactory httpClientFactory, ILogger<SmsIrSmsProvider> logger) : JsonSmsProvider(httpClientFactory, logger)
{
    public override string Key => "smsir";
    public override string DisplayName => "اس‌ام‌اس‌دات‌آی‌آر (SMS.ir)";
    public override string DefaultBaseUrl => "https://api.sms.ir";

    public override async Task<Result<string>> SendAsync(SmsProviderSettings settings, string phoneNumber, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.SenderNumber))
        {
            return Result.Failure<string>(Error.Validation("SMS.ir به کلید API و شماره خط نیاز دارد."));
        }

        var payload = new { lineNumber = long.TryParse(settings.SenderNumber.Trim(), out var line) ? (object)line : settings.SenderNumber.Trim(), messageText = text, mobiles = new[] { phoneNumber } };
        return await CallAsync(
            async http =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseOf(settings, DefaultBaseUrl)}/v1/send/bulk") { Content = JsonContent.Create(payload) };
                request.Headers.TryAddWithoutValidation("X-API-KEY", settings.ApiKey);
                return await http.SendAsync(request, cancellationToken);
            },
            (json, _) =>
            {
                var status = json.TryGetProperty("status", out var s) && s.TryGetInt32(out var code) ? code : 0;
                if (status == 1)
                {
                    return Result.Success(json.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("packId", out var p) ? p.ToString() : "accepted");
                }

                return Refused("SMS.ir", json.TryGetProperty("message", out var m) ? m.GetString() : null);
            });
    }
}
