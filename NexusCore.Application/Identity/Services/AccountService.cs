using System.Security.Cryptography;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusCore.Application.Common;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Identity.Security;
using NexusCore.Application.Messaging;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Services;

public interface IAccountService
{
    Task<Result<ForgotPasswordResponse>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);
    Task<Result<VerifyResetCodeResponse>> VerifyResetCodeAsync(VerifyResetCodeRequest request, CancellationToken cancellationToken);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
    Task<Result<UserDto>> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken);
    Task<Result<UserDto>> UpdateMyPreferencesAsync(UpdateMyPreferencesRequest request, CancellationToken cancellationToken);
    Task<Result> ChangeMyPasswordAsync(ChangeMyPasswordRequest request, CancellationToken cancellationToken, string? currentRefreshToken = null);
    Task<Result<PhoneChangeRequestedResponse>> RequestPhoneChangeAsync(RequestPhoneChangeRequest request, CancellationToken cancellationToken);
    Task<Result<UserDto>> ConfirmPhoneChangeAsync(ConfirmPhoneChangeRequest request, CancellationToken cancellationToken, string? currentRefreshToken = null);
}

/// <summary>
/// What a user does for their own account: reset a forgotten password, change it, keep their
/// profile and preferences. Accounts are only ever created by an administrator
/// (IdentityService.CreateUserAsync); there is no self-registration.
/// </summary>
public sealed class AccountService(
    IIdentityRepository repository,
    ILoginProtection loginProtection,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IPlatformService platformService,
    ICurrentUserContext currentUser,
    ISmsSender smsSender,
    ISmsTemplateService smsTemplates,
    IOptions<PasswordRecoveryOptions> recoveryOptions,
    IValidator<ResetPasswordRequest> resetValidator,
    IValidator<UpdateMyProfileRequest> profileValidator,
    IValidator<ChangeMyPasswordRequest> changePasswordValidator,
    IPhoneChangeCodes phoneChangeCodes,
    ILogger<AccountService> logger) : IAccountService
{
    /// <summary>The answer to every well-formed recovery request, whether or not an account matches.</summary>
    public const string RecoveryRequestedMessage =
        "در صورت وجود حساب کاربری معتبر، کد بازیابی به شماره تلفن ثبت‌شده ارسال خواهد شد.";

    public static readonly Error InvalidCode = new("reset_code.invalid", "کد تأیید صحیح نیست.");
    public static readonly Error ExpiredCode = new("reset_code.expired", "اعتبار کد تأیید به پایان رسیده است. لطفاً کد جدید دریافت کنید.");
    public static readonly Error InvalidResetToken = new("reset_token.invalid", "مهلت تعیین رمز عبور جدید به پایان رسیده است. لطفاً دوباره کد بازیابی دریافت کنید.");

    /// <summary>
    /// Step 1: a one-time code by SMS to the account's mobile number. Every well-formed request
    /// gets the same answer; an unknown, disabled or phoneless account simply receives nothing.
    /// </summary>
    public async Task<Result<ForgotPasswordResponse>> ForgotPasswordAsync(
        ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        // An account that exists costs a database write and an SMS gateway call, one that does
        // not costs nothing. Every answer therefore takes at least the same minimum time, so the
        // response time does not tell the two apart.
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await SendRecoveryCodeAsync(request, cancellationToken);
        var floor = TimeSpan.FromMilliseconds(Math.Clamp(recoveryOptions.Value.MinimumResponseMilliseconds, 0, 5000));
        if (started.Elapsed < floor)
        {
            await Task.Delay(floor - started.Elapsed, cancellationToken);
        }

        return result;
    }

    private async Task<Result<ForgotPasswordResponse>> SendRecoveryCodeAsync(
        ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Identifier) || request.Identifier.Length > 256)
        {
            return Result.Failure<ForgotPasswordResponse>(Error.Validation("نام کاربری یا شماره تلفن همراه را وارد کنید."));
        }

        var throttled = await loginProtection.ThrottleAsync(AuthAction.ForgotPassword, cancellationToken);
        if (throttled.IsFailure)
        {
            return Result.Failure<ForgotPasswordResponse>(throttled.Error);
        }

        // Keyed by what was typed, so a limit reached says nothing about any account.
        var limited = await loginProtection.BeforeResetCodeRequestAsync(request.Identifier, cancellationToken);
        if (limited.IsFailure)
        {
            return Result.Failure<ForgotPasswordResponse>(limited.Error);
        }

        var options = recoveryOptions.Value;
        var answer = new ForgotPasswordResponse(RecoveryRequestedMessage, Math.Max(1, options.CodeLifetimeMinutes) * 60);

        var user = await repository.FindUserByLoginAsync(request.Identifier, request.TenantSlug, cancellationToken);
        // A disabled account cannot get back in this way; without a mobile number there is
        // nowhere to send the code. The answer stays the same.
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            return Result.Success(answer);
        }

        // The same pace and cap per account, so asking by username and by mobile number in turn
        // does not double them. Applied silently: a visible refusal here would confirm the account.
        if ((await loginProtection.BeforeResetCodeRequestAsync($"account:{user.Id:N}", cancellationToken)).IsFailure)
        {
            return Result.Success(answer);
        }

        var now = DateTimeOffset.UtcNow;
        var code = GenerateCode(Math.Clamp(options.CodeLength, 4, 8));
        var tokenId = Guid.NewGuid();

        // A new code supersedes every older code or pending reset.
        await repository.InvalidatePasswordResetTokensAsync(user.Id, now, cancellationToken);
        await repository.AddPasswordResetTokenAsync(
            new PasswordResetToken(tokenId, user.Id, CodeHash(tokenId, code), now.AddMinutes(Math.Max(1, options.CodeLifetimeMinutes)), currentUser.IpAddress),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The wording is the tenant's "password_reset" SMS template; it always carries {Code}.
        var lifetimeMinutes = Math.Max(1, options.CodeLifetimeMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rendered = await smsTemplates.RenderAsync(user.TenantId, SmsTemplateKeys.PasswordReset,
            new Dictionary<string, string?> { ["Code"] = code, ["ExpireMinutes"] = lifetimeMinutes }, cancellationToken);
        var text = rendered.IsSuccess
            ? rendered.Value!
            : $"کد بازیابی رمز عبور شما: {code}\nاین کد تا {lifetimeMinutes} دقیقه معتبر است.";
        var sent = await smsSender.SendAsync(user.TenantId, user.PhoneNumber, text, cancellationToken);
        if (sent.IsFailure)
        {
            // A code that never arrived must not stay usable. Never logged: the code itself.
            await repository.InvalidatePasswordResetTokensAsync(user.Id, DateTimeOffset.UtcNow, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Password recovery code for user {UserId} could not be sent by SMS ({ErrorCode}).", user.Id, sent.Error.Code);
            return Result.Success(answer);
        }

        await platformService.AuditForAsync(user.TenantId, user.Id, "users.password_reset_code_sent", nameof(User), user.Id.ToString(), null, cancellationToken);
        return Result.Success(answer);
    }

    /// <summary>
    /// Step 2: checks the SMS code. A wrong code and "no such account" give the same answer;
    /// "expired" is only said to someone who typed the right code. The identifier has a limited
    /// number of attempts, after which the outstanding code is withdrawn.
    /// </summary>
    public async Task<Result<VerifyResetCodeResponse>> VerifyResetCodeAsync(
        VerifyResetCodeRequest request, CancellationToken cancellationToken)
    {
        var code = NormalizeDigits(request.Code);
        if (string.IsNullOrWhiteSpace(request.Identifier) || request.Identifier.Length > 256 || code.Length is 0 or > 16)
        {
            return Result.Failure<VerifyResetCodeResponse>(InvalidCode);
        }

        var throttled = await loginProtection.ThrottleAsync(AuthAction.ResetCode, cancellationToken);
        if (throttled.IsFailure)
        {
            return Result.Failure<VerifyResetCodeResponse>(throttled.Error);
        }

        var limited = await loginProtection.BeforeResetCodeAttemptAsync(request.Identifier, cancellationToken);
        if (limited.IsFailure)
        {
            return Result.Failure<VerifyResetCodeResponse>(limited.Error);
        }

        var user = await repository.FindUserByLoginAsync(request.Identifier, request.TenantSlug, cancellationToken);
        var token = user is { IsActive: true }
            ? await repository.FindLatestOutstandingPasswordResetTokenAsync(user.Id, cancellationToken)
            : null;

        if (token is null || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(CodeHash(token.Id, code)),
                System.Text.Encoding.ASCII.GetBytes(token.TokenHash)))
        {
            var lastAttempt = await loginProtection.RecordFailedResetCodeAsync(request.Identifier, cancellationToken);
            if (lastAttempt && user is not null)
            {
                await repository.InvalidatePasswordResetTokensAsync(user.Id, DateTimeOffset.UtcNow, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            if (user is not null)
            {
                await platformService.AuditForAsync(user.TenantId, user.Id, "users.password_reset_code_failed", nameof(User), user.Id.ToString(), null, cancellationToken);
            }

            return Result.Failure<VerifyResetCodeResponse>(InvalidCode);
        }

        var now = DateTimeOffset.UtcNow;
        if (token.ExpiresAtUtc <= now)
        {
            return Result.Failure<VerifyResetCodeResponse>(ExpiredCode);
        }

        // The code is spent. What it buys is a short-lived token for setting the new password,
        // stored hashed in the same table like every reset token.
        token.MarkUsed(now);
        var resetToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAtUtc = now.AddMinutes(Math.Max(1, recoveryOptions.Value.ResetTokenLifetimeMinutes));
        await repository.AddPasswordResetTokenAsync(
            new PasswordResetToken(Guid.NewGuid(), token.UserId, passwordHasher.HashToken(resetToken), expiresAtUtc, currentUser.IpAddress),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await loginProtection.ClearResetCodeFailuresAsync(request.Identifier, cancellationToken);
        await platformService.AuditForAsync(user!.TenantId, user.Id, "users.password_reset_code_verified", nameof(User), user.Id.ToString(), null, cancellationToken);

        return Result.Success(new VerifyResetCodeResponse(resetToken, expiresAtUtc));
    }

    /// <summary>Step 3: the new password, hashed like every password; every session of the user ends.</summary>
    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var validation = await resetValidator.ValidateAsResultAsync(request, cancellationToken);
        if (validation.IsFailure)
        {
            return validation;
        }

        var token = await repository.FindActivePasswordResetTokenAsync(passwordHasher.HashToken(request.Token.Trim()), cancellationToken);
        if (token?.User is null || !token.User.IsActive)
        {
            return Result.Failure(InvalidResetToken);
        }

        var now = DateTimeOffset.UtcNow;
        token.User.ChangePassword(passwordHasher.HashPassword(request.NewPassword));
        token.MarkUsed(now);
        await repository.InvalidatePasswordResetTokensAsync(token.UserId, now, cancellationToken);
        // The new password signs the user out of every existing session.
        await repository.RevokeRefreshTokensAsync(token.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditForAsync(token.User.TenantId, token.UserId, "users.password_reset", nameof(User), token.UserId.ToString(), null, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// A code is only ever compared through its hash, salted with its own token id, so the table
    /// never holds a code and a code cannot be matched against another user's token.
    /// </summary>
    private string CodeHash(Guid tokenId, string code) => passwordHasher.HashToken($"reset-code:{tokenId:N}:{code}");

    private static string GenerateCode(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(_ => (char)('0' + RandomNumberGenerator.GetInt32(10))));

    /// <summary>Persian and Arabic-Indic digits count as the digits they are; spaces are dropped.</summary>
    private static string NormalizeDigits(string? value)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var ch in value ?? string.Empty)
        {
            if (ch is >= '۰' and <= '۹') builder.Append((char)('0' + (ch - '۰')));
            else if (ch is >= '٠' and <= '٩') builder.Append((char)('0' + (ch - '٠')));
            else if (!char.IsWhiteSpace(ch)) builder.Append(ch);
        }

        return builder.ToString();
    }

    public async Task<Result<UserDto>> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<UserDto>(Error.Unauthorized());
        }

        var validation = await profileValidator.ValidateAsResultAsync(request, cancellationToken);
        if (validation.IsFailure)
        {
            return Result.Failure<UserDto>(validation.Error);
        }

        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(Error.NotFound("کاربر یافت نشد."));
        }

        // The mobile number is how a forgotten password is recovered, so it is not changed by a
        // plain profile save: a stolen session could otherwise point recovery at the thief's
        // phone. It changes only through the SMS-verified steps (auth/me/phone/request, /confirm).
        // An empty or unchanged value here simply keeps the current number.
        var requestedPhone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? user.PhoneNumber : PhoneNumber.Normalize(request.PhoneNumber);
        if (!string.Equals(requestedPhone, user.PhoneNumber, StringComparison.Ordinal))
        {
            return Result.Failure<UserDto>(Error.Forbidden("Changing the mobile number needs a verification code sent to the new number. Use the \"change mobile number\" step."));
        }

        // Identity fields are immutable through the self-service endpoint, even if a caller
        // bypasses the UI and submits an older payload directly.
        if (!string.Equals(request.DisplayName.Trim(), user.DisplayName, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(request.FirstName)
                && !string.Equals(request.FirstName.Trim(), user.FirstName, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(request.LastName)
                && !string.Equals(request.LastName.Trim(), user.LastName, StringComparison.Ordinal)))
        {
            return Result.Failure<UserDto>(Error.Forbidden("اطلاعات هویتی از پروفایل شخصی تغییر نمی‌کند."));
        }

        user.UpdateContactDetails(user.Username, user.PhoneNumber, request.NotifySms);
        user.SetAvatar(request.AvatarUrl);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("users.update_own_profile", nameof(User), user.Id.ToString(), null, cancellationToken);
        return Result.Success(IdentityMappings.ToUserDto(user));
    }

    public async Task<Result<UserDto>> UpdateMyPreferencesAsync(UpdateMyPreferencesRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<UserDto>(Error.Unauthorized());
        }

        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(Error.NotFound("کاربر یافت نشد."));
        }

        user.SetPreferences(Limit(request.Theme, 40), Limit(request.ColorPalette, 40), Limit(request.ThemeMode, 10));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(IdentityMappings.ToUserDto(user));
    }

    /// <summary>
    /// The signed-in user changes their own password, proving the current one. This is also how
    /// the built-in system administrator changes theirs - nobody else can.
    /// </summary>
    public async Task<Result> ChangeMyPasswordAsync(ChangeMyPasswordRequest request, CancellationToken cancellationToken, string? currentRefreshToken = null)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var validation = await changePasswordValidator.ValidateAsResultAsync(request, cancellationToken);
        if (validation.IsFailure)
        {
            return validation;
        }

        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("کاربر یافت نشد."));
        }

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure(Error.Validation("رمز عبور فعلی صحیح نیست."));
        }

        user.ChangePassword(passwordHasher.HashPassword(request.NewPassword));
        // Whoever else holds a session (a stolen one included) is signed out; the one that
        // changed the password carries on.
        await EndOtherSessionsAsync(user.Id, currentRefreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("users.change_own_password", nameof(User), user.Id.ToString(), null, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Revokes the user's refresh tokens except the sign-in the caller is using, which is known
    /// when its refresh token (cookie) came with the request. Without it nothing says which
    /// sign-in is the caller's, so all of them end.
    /// </summary>
    private async Task EndOtherSessionsAsync(Guid userId, string? currentRefreshToken, CancellationToken cancellationToken)
    {
        var current = string.IsNullOrWhiteSpace(currentRefreshToken)
            ? null
            : await repository.FindRefreshTokenAsync(passwordHasher.HashToken(currentRefreshToken), cancellationToken);
        if (current is not null && current.UserId == userId && current.RevokedAtUtc is null && current.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            await repository.RevokeRefreshTokensExceptFamilyAsync(userId, current.Family, cancellationToken);
        }
        else
        {
            await repository.RevokeRefreshTokensAsync(userId, cancellationToken);
        }
    }

    // ---------------------------------------------------------------- changing the mobile number

    private static string PhoneChangeKey(Guid userId) => $"phone-change:{userId:N}";

    /// <summary>
    /// Step 1: proves the password and sends a one-time code to the NEW number. Nothing on the
    /// account changes yet.
    /// </summary>
    public async Task<Result<PhoneChangeRequestedResponse>> RequestPhoneChangeAsync(
        RequestPhoneChangeRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<PhoneChangeRequestedResponse>(Error.Unauthorized());
        }

        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<PhoneChangeRequestedResponse>(Error.NotFound("کاربر یافت نشد."));
        }

        var newPhone = PhoneNumber.Normalize(request.NewPhoneNumber);
        if (newPhone is null)
        {
            return Result.Failure<PhoneChangeRequestedResponse>(Error.Validation("شماره تلفن همراه معتبر وارد کنید (مثلاً ۰۹۱۲۱۲۳۴۵۶۷ یا ‎+98 912 123 4567‎)."));
        }

        var throttled = await loginProtection.ThrottleAsync(AuthAction.PhoneChange, cancellationToken);
        if (throttled.IsFailure)
        {
            return Result.Failure<PhoneChangeRequestedResponse>(throttled.Error);
        }

        var password = await CheckPasswordWithLimitAsync(user, request.CurrentPassword, cancellationToken);
        if (password.IsFailure)
        {
            return Result.Failure<PhoneChangeRequestedResponse>(password.Error);
        }

        if (newPhone == user.PhoneNumber)
        {
            return Result.Failure<PhoneChangeRequestedResponse>(Error.Validation("این شماره همین حالا شماره‌ی تلفن همراه شماست."));
        }

        if (await repository.PhoneNumberExistsAsync(user.TenantId, newPhone, user.Id, cancellationToken))
        {
            return Result.Failure<PhoneChangeRequestedResponse>(Error.Conflict("این شماره تلفن همراه قابل استفاده نیست. شماره دیگری وارد کنید."));
        }

        var limited = await loginProtection.BeforeResetCodeRequestAsync(PhoneChangeKey(userId), cancellationToken);
        if (limited.IsFailure)
        {
            return Result.Failure<PhoneChangeRequestedResponse>(limited.Error);
        }

        var options = recoveryOptions.Value;
        var lifetime = TimeSpan.FromMinutes(Math.Max(1, options.CodeLifetimeMinutes));
        var code = GenerateCode(Math.Clamp(options.CodeLength, 4, 8));
        await phoneChangeCodes.StoreAsync(userId, newPhone, code, lifetime, cancellationToken);

        var minutes = ((int)lifetime.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rendered = await smsTemplates.RenderAsync(user.TenantId, SmsTemplateKeys.PhoneChange,
            new Dictionary<string, string?> { ["Code"] = code, ["ExpireMinutes"] = minutes }, cancellationToken);
        var text = rendered.IsSuccess
            ? rendered.Value!
            : $"کد تأیید شماره موبایل جدید شما: {code}\nاین کد تا {minutes} دقیقه معتبر است.";
        var sent = await smsSender.SendAsync(user.TenantId, newPhone, text, cancellationToken);
        if (sent.IsFailure)
        {
            await phoneChangeCodes.ClearAsync(userId, cancellationToken);
            logger.LogWarning("Phone change code for user {UserId} could not be sent by SMS ({ErrorCode}).", userId, sent.Error.Code);
            return Result.Failure<PhoneChangeRequestedResponse>(new Error("sms.unavailable", "کد تأیید ارسال نشد؛ کمی بعد دوباره تلاش کنید."));
        }

        await platformService.AuditAsync("users.phone_change_code_sent", nameof(User), userId.ToString(), null, cancellationToken);
        return Result.Success(new PhoneChangeRequestedResponse("کد تأیید به شماره‌ی جدید ارسال شد.", (int)lifetime.TotalSeconds));
    }

    /// <summary>
    /// Step 2: the password again and the code that reached the new number. Only then the number
    /// on the account changes, and every other sign-in of the user ends.
    /// </summary>
    public async Task<Result<UserDto>> ConfirmPhoneChangeAsync(
        ConfirmPhoneChangeRequest request, CancellationToken cancellationToken, string? currentRefreshToken = null)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<UserDto>(Error.Unauthorized());
        }

        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(Error.NotFound("کاربر یافت نشد."));
        }

        var newPhone = PhoneNumber.Normalize(request.NewPhoneNumber);
        var code = NormalizeDigits(request.Code);
        if (newPhone is null || code.Length is 0 or > 16)
        {
            return Result.Failure<UserDto>(InvalidCode);
        }

        var throttled = await loginProtection.ThrottleAsync(AuthAction.ResetCode, cancellationToken);
        if (throttled.IsFailure)
        {
            return Result.Failure<UserDto>(throttled.Error);
        }

        var password = await CheckPasswordWithLimitAsync(user, request.CurrentPassword, cancellationToken);
        if (password.IsFailure)
        {
            return Result.Failure<UserDto>(password.Error);
        }

        var key = PhoneChangeKey(userId);
        if (!await phoneChangeCodes.ConfirmAsync(userId, newPhone, code, cancellationToken))
        {
            if (await loginProtection.RecordFailedResetCodeAsync(key, cancellationToken))
            {
                await phoneChangeCodes.ClearAsync(userId, cancellationToken);
            }

            await platformService.AuditAsync("users.phone_change_code_failed", nameof(User), userId.ToString(), null, cancellationToken);
            return Result.Failure<UserDto>(InvalidCode);
        }

        // Someone else may have taken the number since the code was sent.
        if (await repository.PhoneNumberExistsAsync(user.TenantId, newPhone, user.Id, cancellationToken))
        {
            return Result.Failure<UserDto>(Error.Conflict("این شماره تلفن همراه قابل استفاده نیست. شماره دیگری وارد کنید."));
        }

        var previous = user.PhoneNumber;
        user.UpdateContactDetails(user.Username, newPhone, user.NotifySms);
        await EndOtherSessionsAsync(userId, currentRefreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await loginProtection.ClearResetCodeFailuresAsync(key, cancellationToken);
        await platformService.AuditAsync("users.change_phone", nameof(User), userId.ToString(),
            $"{MaskPhone(previous)} -> {MaskPhone(newPhone)}", cancellationToken);
        return Result.Success(IdentityMappings.ToUserDto(user));
    }

    /// <summary>
    /// The current password, with an attempt limit of its own: a stolen session cannot be used to
    /// guess the password here without end. Wrong answers are counted per user.
    /// </summary>
    private async Task<Result> CheckPasswordWithLimitAsync(User user, string? password, CancellationToken cancellationToken)
    {
        var key = PhoneChangeKey(user.Id) + ":password";
        var limited = await loginProtection.BeforeResetCodeAttemptAsync(key, cancellationToken);
        if (limited.IsFailure)
        {
            return limited;
        }

        if (string.IsNullOrEmpty(password) || !passwordHasher.Verify(password, user.PasswordHash))
        {
            await loginProtection.RecordFailedResetCodeAsync(key, cancellationToken);
            return Result.Failure(Error.Validation("رمز عبور فعلی صحیح نیست."));
        }

        await loginProtection.ClearResetCodeFailuresAsync(key, cancellationToken);
        return Result.Success();
    }

    private static string MaskPhone(string? phone) =>
        string.IsNullOrEmpty(phone) ? "-" : phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}
