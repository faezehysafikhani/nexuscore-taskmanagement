using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Identity.Services;
using NexusCore.Application.Messaging;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Security;

public interface IMfaService
{
    /// <summary>After the password was right: null when the user signs in without a code, otherwise the sign-in waits (a code was sent) or is refused.</summary>
    Task<Result<AuthResponse>?> ChallengeIfRequiredAsync(User user, CancellationToken cancellationToken);

    Task<Result<AuthResponse>> CompleteSignInAsync(CompleteMfaRequest request, CancellationToken cancellationToken);

    /// <summary>The signed-in user asks to turn the code on: a code is sent to their mobile number to prove it works.</summary>
    Task<Result<MfaChallengeDto>> RequestEnableAsync(CancellationToken cancellationToken);
    Task<Result<UserDto>> ConfirmEnableAsync(ConfirmMfaEnableRequest request, CancellationToken cancellationToken);
    Task<Result<UserDto>> DisableAsync(DisableMfaRequest request, CancellationToken cancellationToken);

    /// <summary>An administrator switches the code off for someone who lost their phone.</summary>
    Task<Result<UserDto>> ResetAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// The second step of sign-in (phase 9b): after the password, a one-time code is sent by SMS and must be typed back. The code is
/// kept only as a hash, lives a few minutes, is used once, and allows five wrong tries. Whoever has it turned on, or everybody
/// when Identity:Mfa:Required is set (the built-in administrator excepted, so a missing mobile number never locks everyone out).
/// </summary>
public sealed class MfaService(
    ISecurityRepository security,
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IServiceProvider services,
    IPlatformService platformService,
    AuthSessionIssuer sessionIssuer,
    IOptions<MfaOptions> options) : IMfaService
{
    public static readonly Error InvalidCode = new("mfa.invalid_code", "کد وارد‌شده صحیح نیست.");
    public static readonly Error ExpiredCode = new("mfa.expired", "اعتبار کد تمام شده است؛ دوباره وارد شوید.");
    public static readonly Error NoPhone = new("mfa.no_phone", "برای ورود دومرحله‌ای شماره موبایل ثبت‌شده لازم است. با مدیر سامانه تماس بگیرید.");
    public static readonly Error SendFailed = new("mfa.send_failed", "ارسال کد پیامکی ممکن نشد؛ کمی بعد دوباره تلاش کنید.");

    // Looked up when a code is sent, not at construction: a host with no SMS panel can still compose the sign-in.
    private ISmsSender smsSender => services.GetRequiredService<ISmsSender>();
    private ISmsTemplateService smsTemplates => services.GetRequiredService<ISmsTemplateService>();

    private TimeSpan Lifetime => TimeSpan.FromMinutes(Math.Max(1, options.Value.CodeLifetimeMinutes));

    private static bool Exempt(User user) => user.IsSystem || user.Kind == UserKind.ServiceAccount;

    public async Task<Result<AuthResponse>?> ChallengeIfRequiredAsync(User user, CancellationToken cancellationToken)
    {
        if (Exempt(user) || !(user.MfaEnabled || options.Value.Required))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            return Result.Failure<AuthResponse>(NoPhone);
        }

        var sent = await SendCodeAsync(user, MfaPurpose.SignIn, cancellationToken);
        if (sent.IsFailure)
        {
            return Result.Failure<AuthResponse>(sent.Error);
        }

        // No token yet: the answer only says where the code went.
        return Result.Success(new AuthResponse(string.Empty, string.Empty, DateTimeOffset.UtcNow, IdentityMappings.ToUserDto(user), null, null, null, sent.Value));
    }

    private async Task<Result<MfaChallengeDto>> SendCodeAsync(User user, MfaPurpose purpose, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var code = string.Concat(Enumerable.Range(0, Math.Clamp(options.Value.CodeLength, 4, 8)).Select(_ => (char)('0' + RandomNumberGenerator.GetInt32(10))));
        var id = Guid.NewGuid();
        await security.InvalidateChallengesAsync(user.Id, purpose, now, cancellationToken);
        await security.AddChallengeAsync(new MfaChallenge(id, user.Id, purpose, Hash(id, code), now, Lifetime, currentUser.IpAddress), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var minutes = ((int)Lifetime.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rendered = await smsTemplates.RenderAsync(user.TenantId, SmsTemplateKeys.MfaCode, new Dictionary<string, string?> { ["Code"] = code, ["ExpireMinutes"] = minutes }, cancellationToken);
        var text = rendered.IsSuccess ? rendered.Value! : $"کد ورود شما: {code}\nاین کد تا {minutes} دقیقه معتبر است.";
        var sent = await smsSender.SendAsync(user.TenantId, user.PhoneNumber!, text, cancellationToken);
        if (sent.IsFailure)
        {
            // A code that never arrived must not stay usable. The code itself is never logged.
            await security.InvalidateChallengesAsync(user.Id, purpose, DateTimeOffset.UtcNow, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<MfaChallengeDto>(SendFailed);
        }

        return Result.Success(new MfaChallengeDto(id, Mask(user.PhoneNumber!), (int)Lifetime.TotalSeconds));
    }

    public async Task<Result<AuthResponse>> CompleteSignInAsync(CompleteMfaRequest request, CancellationToken cancellationToken)
    {
        var verified = await VerifyAsync(request.ChallengeId, request.Code, MfaPurpose.SignIn, cancellationToken);
        if (verified.IsFailure)
        {
            return Result.Failure<AuthResponse>(verified.Error);
        }

        var user = verified.Value!;
        if (!user.IsActive)
        {
            return Result.Failure<AuthResponse>(IdentityService.AccountDisabled);
        }

        return Result.Success(await sessionIssuer.IssueAsync(user, cancellationToken));
    }

    /// <summary>Checks a code and uses the challenge up; a wrong code counts against it, and after five the challenge is dead.</summary>
    private async Task<Result<User>> VerifyAsync(Guid challengeId, string? rawCode, MfaPurpose purpose, CancellationToken cancellationToken)
    {
        var challenge = await security.GetChallengeAsync(challengeId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (challenge is null || challenge.Purpose != purpose || challenge.UsedAtUtc is not null || challenge.Attempts >= MfaChallenge.MaxAttempts)
        {
            return Result.Failure<User>(InvalidCode);
        }

        if (now >= challenge.ExpiresAtUtc)
        {
            return Result.Failure<User>(ExpiredCode);
        }

        var code = Normalize(rawCode);
        var ok = code.Length is > 0 and <= 16 && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(challenge.Id, code)), Encoding.ASCII.GetBytes(challenge.CodeHash));
        var user = await repository.GetUserByIdAsync(challenge.UserId, cancellationToken);
        if (!ok || user is null)
        {
            challenge.RecordWrongAttempt();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (user is not null)
            {
                await platformService.AuditForAsync(user.TenantId, user.Id, "identity.mfa_failed", nameof(User), user.Id.ToString(), null, cancellationToken);
            }

            return Result.Failure<User>(InvalidCode);
        }

        challenge.MarkUsed(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(user);
    }

    public async Task<Result<MfaChallengeDto>> RequestEnableAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await repository.GetUserByIdAsync(userId, cancellationToken) is not { } user)
        {
            return Result.Failure<MfaChallengeDto>(Error.Unauthorized());
        }

        return string.IsNullOrWhiteSpace(user.PhoneNumber)
            ? Result.Failure<MfaChallengeDto>(NoPhone)
            : await SendCodeAsync(user, MfaPurpose.Enable, cancellationToken);
    }

    public async Task<Result<UserDto>> ConfirmEnableAsync(ConfirmMfaEnableRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<UserDto>(Error.Unauthorized());
        }

        var verified = await VerifyAsync(request.ChallengeId, request.Code, MfaPurpose.Enable, cancellationToken);
        if (verified.IsFailure || verified.Value!.Id != userId)
        {
            return Result.Failure<UserDto>(verified.IsFailure ? verified.Error : InvalidCode);
        }

        verified.Value.SetMfaEnabled(true);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditForAsync(verified.Value.TenantId, userId, "identity.mfa_enabled", nameof(User), userId.ToString(), null, cancellationToken);
        return Result.Success(IdentityMappings.ToUserDto(verified.Value));
    }

    public async Task<Result<UserDto>> DisableAsync(DisableMfaRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await repository.GetUserByIdAsync(userId, cancellationToken) is not { } user)
        {
            return Result.Failure<UserDto>(Error.Unauthorized());
        }

        if (!passwordHasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
        {
            return Result.Failure<UserDto>(Error.Validation("رمز عبور صحیح نیست."));
        }

        user.SetMfaEnabled(false);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditForAsync(user.TenantId, userId, "identity.mfa_disabled", nameof(User), userId.ToString(), null, cancellationToken);
        return Result.Success(IdentityMappings.ToUserDto(user));
    }

    public async Task<Result<UserDto>> ResetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null || currentUser.TenantId is not { } tenant || user.TenantId != tenant)
        {
            return Result.Failure<UserDto>(Error.NotFound("کاربر یافت نشد."));
        }

        user.SetMfaEnabled(false);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditForAsync(user.TenantId, currentUser.UserId, "identity.mfa_reset", nameof(User), userId.ToString(), null, cancellationToken);
        return Result.Success(IdentityMappings.ToUserDto(user));
    }

    private string Hash(Guid id, string code) => passwordHasher.HashToken($"mfa:{id:N}:{code}");

    private static string Mask(string phone) => phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];

    private static string Normalize(string? value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value ?? string.Empty)
        {
            if (ch is >= '۰' and <= '۹') builder.Append((char)('0' + (ch - '۰')));
            else if (ch is >= '٠' and <= '٩') builder.Append((char)('0' + (ch - '٠')));
            else if (!char.IsWhiteSpace(ch)) builder.Append(ch);
        }

        return builder.ToString();
    }
}
