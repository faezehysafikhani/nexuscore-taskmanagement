using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Security;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Security;

/// <summary>
/// <see cref="ILoginProtection"/> on <see cref="IDistributedCache"/>: in-memory by default, and
/// shared between instances as soon as the host registers a distributed cache (Redis, SQL).
///
/// Keys hold hashes only - never an IP address, username or phone number in clear - and every
/// entry expires by itself. CAPTCHA answers are stored as a salted hash, bound to the client
/// that asked for the CAPTCHA, and removed on the first attempt to use them.
///
/// The counters are read-modify-write; two simultaneous requests can both be counted as the
/// first. That can let one extra attempt through, never lock anyone out wrongly.
/// </summary>
public sealed class LoginProtection(
    IDistributedCache cache,
    ICurrentUserContext requestContext,
    IOptions<LoginProtectionOptions> options) : ILoginProtection
{
    private const string Prefix = "nexuscore:auth:";
    private const string Digits = "0123456789";

    private LoginProtectionOptions Options => options.Value;

    private string ClientKey => Hash("client", requestContext.IpAddress ?? "unknown");

    /// <summary>Makes the code of a new CAPTCHA. Replaced only by tests, which cannot read the image.</summary>
    internal Func<int, string> GenerateCode { get; init; } = length =>
        string.Concat(Enumerable.Range(0, length).Select(_ => Digits[RandomNumberGenerator.GetInt32(Digits.Length)]));

    public async Task<Result<CaptchaChallengeDto>> IssueCaptchaAsync(CancellationToken cancellationToken)
    {
        var throttled = await ThrottleAsync(AuthAction.Captcha, cancellationToken);
        if (throttled.IsFailure)
        {
            return Result.Failure<CaptchaChallengeDto>(throttled.Error);
        }

        var length = Math.Clamp(Options.CaptchaLength, 4, 8);
        var code = GenerateCode(length);
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var lifetime = TimeSpan.FromSeconds(Math.Clamp(Options.CaptchaLifetimeSeconds, 30, 900));

        var record = new CaptchaRecord(AnswerHash(id, code), ClientKey);
        await cache.SetStringAsync(CaptchaKey(id), JsonSerializer.Serialize(record),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime }, cancellationToken);

        var image = "data:image/png;base64," + Convert.ToBase64String(CaptchaImage.Render(code));
        return Result.Success(new CaptchaChallengeDto(id, image, (int)lifetime.TotalSeconds));
    }

    public async Task<Result> BeforeLoginAttemptAsync(
        string identifier, string? captchaId, string? captchaAnswer, CancellationToken cancellationToken)
    {
        var throttled = await ThrottleAsync(AuthAction.Login, cancellationToken);
        if (throttled.IsFailure)
        {
            return throttled;
        }

        var failures = await ReadCounterAsync(IdentifierKey(identifier), cancellationToken);
        if (failures >= Options.MaxFailedAttemptsPerIdentifier)
        {
            // Same answer for an existing and a non-existing account: the counter is keyed by
            // what was typed, not by a user.
            return Result.Failure(Error.TooManyRequests(
                "Too many failed sign-in attempts. Please wait a while before trying again."));
        }

        if (!await IsCaptchaRequiredAsync(failures, cancellationToken))
        {
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(captchaId) || string.IsNullOrWhiteSpace(captchaAnswer))
        {
            return Result.Failure(new Error("captcha.required", "Complete the CAPTCHA to sign in."));
        }

        return await VerifyCaptchaAsync(captchaId.Trim(), captchaAnswer, cancellationToken)
            ? Result.Success()
            : Result.Failure(new Error("captcha.invalid", "The CAPTCHA answer is wrong or has expired. Try the new one."));
    }

    public async Task<bool> RecordFailedLoginAsync(string identifier, CancellationToken cancellationToken)
    {
        var window = TimeSpan.FromMinutes(Math.Max(1, Options.FailureWindowMinutes));
        var byIdentifier = await IncrementAsync(IdentifierKey(identifier), window, cancellationToken);
        var byClient = await IncrementAsync(ClientFailureKey, window, cancellationToken);
        return Math.Max(byIdentifier, byClient) >= Math.Max(1, Options.CaptchaAfterFailedAttempts);
    }

    public async Task RecordSuccessfulLoginAsync(string identifier, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(IdentifierKey(identifier), cancellationToken);
        await cache.RemoveAsync(ClientFailureKey, cancellationToken);
    }

    public async Task<Result> ThrottleAsync(AuthAction action, CancellationToken cancellationToken)
    {
        var o = Options;
        var (max, minutes) = action switch
        {
            AuthAction.Login => (o.MaxLoginAttemptsPerClient, o.LoginWindowMinutes),
            AuthAction.Captcha => (o.MaxCaptchasPerClient, o.CaptchaWindowMinutes),
            AuthAction.ForgotPassword => (o.MaxPasswordResetRequestsPerClient, o.PasswordResetWindowMinutes),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        var count = await IncrementAsync($"{Prefix}rate:{action}:{ClientKey}", TimeSpan.FromMinutes(Math.Max(1, minutes)), cancellationToken);
        return count > Math.Max(1, max) ? Result.Failure(Error.TooManyRequests()) : Result.Success();
    }

    private async Task<bool> IsCaptchaRequiredAsync(int identifierFailures, CancellationToken cancellationToken)
    {
        var threshold = Math.Max(1, Options.CaptchaAfterFailedAttempts);
        return identifierFailures >= threshold
               || await ReadCounterAsync(ClientFailureKey, cancellationToken) >= threshold;
    }

    private async Task<bool> VerifyCaptchaAsync(string captchaId, string answer, CancellationToken cancellationToken)
    {
        if (captchaId.Length > 64)
        {
            return false;
        }

        var key = CaptchaKey(captchaId);
        var stored = await cache.GetStringAsync(key, cancellationToken);
        if (stored is null)
        {
            return false;
        }

        // Single use: gone before it is even compared, so a wrong answer cannot be retried and a
        // right one cannot be replayed.
        await cache.RemoveAsync(key, cancellationToken);

        var record = JsonSerializer.Deserialize<CaptchaRecord>(stored);
        if (record is null || record.ClientKey != ClientKey)
        {
            return false;
        }

        var expected = Convert.FromHexString(record.AnswerHash);
        var actual = Convert.FromHexString(AnswerHash(captchaId, NormalizeAnswer(answer)));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private async Task<int> ReadCounterAsync(string key, CancellationToken cancellationToken)
    {
        var raw = await cache.GetStringAsync(key, cancellationToken);
        return raw is null ? 0 : JsonSerializer.Deserialize<Counter>(raw)?.Count ?? 0;
    }

    /// <summary>Fixed window: the first hit opens it, it expires as a whole.</summary>
    private async Task<int> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var raw = await cache.GetStringAsync(key, cancellationToken);
        var counter = raw is null ? null : JsonSerializer.Deserialize<Counter>(raw);
        if (counter is null || counter.WindowEndsAtUtc <= now)
        {
            counter = new Counter(0, now.Add(window));
        }

        counter = counter with { Count = counter.Count + 1 };
        await cache.SetStringAsync(key, JsonSerializer.Serialize(counter),
            new DistributedCacheEntryOptions { AbsoluteExpiration = counter.WindowEndsAtUtc }, cancellationToken);
        return counter.Count;
    }

    private string ClientFailureKey => $"{Prefix}fail:client:{ClientKey}";

    /// <summary>
    /// Keyed by the identifier as typed, in the same canonical form sign-in uses, so "0912..."
    /// and "+98 912..." share one counter.
    /// </summary>
    private static string IdentifierKey(string identifier)
    {
        var value = identifier.Trim();
        var canonical = Username.IsValid(value) ? value : PhoneNumber.Normalize(value) ?? value.ToLowerInvariant();
        return $"{Prefix}fail:id:{Hash("id", canonical)}";
    }

    private static string CaptchaKey(string id) => $"{Prefix}captcha:{id}";

    private static string AnswerHash(string captchaId, string answer) => Hash("captcha:" + captchaId, answer);

    /// <summary>Persian and Arabic-Indic digits count as the digits they are; spaces are ignored.</summary>
    private static string NormalizeAnswer(string answer)
    {
        var builder = new StringBuilder(answer.Length);
        foreach (var ch in answer)
        {
            if (ch is >= '۰' and <= '۹')
            {
                builder.Append((char)('0' + (ch - '۰')));
            }
            else if (ch is >= '٠' and <= '٩')
            {
                builder.Append((char)('0' + (ch - '٠')));
            }
            else if (!char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string Hash(string purpose, string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(purpose + "\n" + value)));

    private sealed record CaptchaRecord(string AnswerHash, string ClientKey);

    private sealed record Counter(int Count, DateTimeOffset WindowEndsAtUtc);
}
