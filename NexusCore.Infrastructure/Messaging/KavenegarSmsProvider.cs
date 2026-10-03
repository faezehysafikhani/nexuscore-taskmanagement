using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Messaging;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Messaging;

/// <summary>
/// Kavenegar (کاوه‌نگار) through its REST API: POST {base}/{apiKey}/sms/send.json with receptor,
/// message and - optionally - sender (without it Kavenegar uses the account's default line).
///
/// The API key is part of the request path by Kavenegar's design, so neither the URL nor an
/// exception text (which can contain it) is ever logged or returned; the HTTP client this uses
/// has request logging removed.
/// </summary>
public sealed class KavenegarSmsProvider(IHttpClientFactory httpClientFactory, ILogger<KavenegarSmsProvider> logger) : ISmsProvider
{
    public const string HttpClientName = "NexusCore.Messaging.Sms";

    public string Key => "kavenegar";
    public string DisplayName => "کاوه‌نگار (Kavenegar)";
    public string DefaultBaseUrl => "https://api.kavenegar.com/v1";

    // Kavenegar's documented status codes, in words an administrator can act on.
    private static readonly Dictionary<int, string> Errors = new()
    {
        [400] = "پارامترهای درخواست ناقص است.",
        [401] = "حساب کاربری کاوه‌نگار غیرفعال شده است.",
        [402] = "عملیات ناموفق بود.",
        [403] = "کلید API معتبر نیست.",
        [404] = "متد درخواستی در کاوه‌نگار یافت نشد؛ آدرس سرویس (Base URL) را بررسی کنید.",
        [405] = "نوع درخواست (GET/POST) برای این متد مجاز نیست.",
        [406] = "پارامترهای اجباری خالی ارسال شده‌اند.",
        [407] = "دسترسی به این اطلاعات برای حساب شما مجاز نیست.",
        [409] = "سرور کاوه‌نگار قادر به پاسخگویی نیست؛ بعداً تلاش کنید.",
        [411] = "شماره گیرنده معتبر نیست.",
        [412] = "شماره فرستنده (خط) معتبر نیست یا متعلق به حساب شما نیست.",
        [413] = "متن پیام خالی یا بیش از حد طولانی است.",
        [414] = "تعداد گیرندگان بیش از حد مجاز است.",
        [416] = "آی‌پی سرور با آی‌پی‌های مجاز حساب کاوه‌نگار مطابقت ندارد.",
        [418] = "اعتبار حساب کاوه‌نگار کافی نیست.",
        [422] = "متن پیام کاراکترهای غیرمجاز دارد.",
        [427] = "استفاده از این خط فرستنده محدود شده است.",
        [451] = "تعداد درخواست‌ها بیش از حد مجاز است؛ کمی بعد تلاش کنید.",
        [501] = "در حساب آزمایشی فقط به شماره صاحب حساب می‌توان پیامک فرستاد.",
    };

    public async Task<Result<string>> SendAsync(SmsProviderSettings settings, string phoneNumber, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return Result.Failure<string>(Error.Validation("کاوه‌نگار برای ارسال پیامک به کلید API نیاز دارد."));
        }

        var baseUrl = (string.IsNullOrWhiteSpace(settings.BaseUrl) ? DefaultBaseUrl : settings.BaseUrl).TrimEnd('/');
        var form = new Dictionary<string, string> { ["receptor"] = phoneNumber, ["message"] = text };
        if (!string.IsNullOrWhiteSpace(settings.SenderNumber))
        {
            form["sender"] = settings.SenderNumber.Trim();
        }

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .PostAsync($"{baseUrl}/{Uri.EscapeDataString(settings.ApiKey)}/sms/send.json", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            JsonDocument? json = null;
            try
            {
                json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            }
            catch (JsonException)
            {
                // Not Kavenegar's JSON: wrong Base URL, a proxy page, ...
            }

            using (json)
            {
                var hasReturn = json is not null && json.RootElement.ValueKind == JsonValueKind.Object
                    && json.RootElement.TryGetProperty("return", out var ret) && ret.ValueKind == JsonValueKind.Object;
                if (!hasReturn)
                {
                    logger.LogWarning("Kavenegar answered HTTP {StatusCode} without its status object.", (int)response.StatusCode);
                    return Result.Failure<string>(Error.Validation(
                        $"پاسخ نامعتبر از سرویس پیامک (HTTP {(int)response.StatusCode}). آدرس سرویس (Base URL) را بررسی کنید."));
                }

                var returned = json!.RootElement.GetProperty("return");
                var status = returned.TryGetProperty("status", out var s) && s.TryGetInt32(out var code) ? code : (int)response.StatusCode;
                if (status is 200 or 201)
                {
                    var messageId = json.RootElement.TryGetProperty("entries", out var entries)
                                    && entries.ValueKind == JsonValueKind.Array
                                    && entries.GetArrayLength() > 0
                                    && entries[0].TryGetProperty("messageid", out var id)
                        ? id.ToString()
                        : "accepted";
                    return Result.Success(messageId);
                }

                var providerMessage = returned.TryGetProperty("message", out var m) ? m.GetString() : null;
                logger.LogWarning("Kavenegar rejected an SMS (status {Status}).", status);
                return Result.Failure<string>(Error.Validation(
                    $"خطای کاوه‌نگار ({status}): {Errors.GetValueOrDefault(status) ?? providerMessage ?? "ارسال پیامک ناموفق بود."}"));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            // The exception message can contain the request URL, and with it the API key.
            logger.LogWarning("SMS gateway request failed: {ErrorType}.", ex.GetType().Name);
            return Result.Failure<string>(Error.Validation("ارتباط با سرویس پیامک برقرار نشد. آدرس سرویس و اتصال شبکه سرور را بررسی کنید."));
        }
    }
}
