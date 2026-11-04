using FluentValidation;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Domain.Identity;

namespace NexusCore.Application.Identity.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Username or mobile number - the repository decides which. Not validated further here:
        // the answer to a malformed identifier is the same "invalid credentials" as to a wrong one.
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
        RuleFor(x => x.CaptchaId).MaximumLength(64);
        RuleFor(x => x.CaptchaAnswer).MaximumLength(16);
    }
}

/// <summary>Shared rules for the sign-in names and profile fields.</summary>
internal static class ProfileRules
{
    public const int MaxAvatarLength = 1_500_000;
    private const int MaxPersonnelPhotoBytes = 300 * 1024;

    // Count decoded bytes, not Base64 characters; HTTP callers cannot bypass the upload limit.
    public static bool IsPersonnelPhotoWithinLimit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var data = value.Trim();
        var comma = data.IndexOf(',');
        if (!data.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || comma < 0) return false;
        var payload = data[(comma + 1)..];
        if (payload.Length > 4 * ((MaxPersonnelPhotoBytes + 2) / 3)) return false;
        try { return Convert.FromBase64String(payload).Length <= MaxPersonnelPhotoBytes; }
        catch (FormatException) { return false; }
    }

    public const string UsernameMessage = "نام کاربری باید کد ملی معتبر ۱۰ رقمی باشد.";
    public const string PhoneMessage = "شماره تلفن همراه معتبر وارد کنید (مثلاً ۰۹۱۲۱۲۳۴۵۶۷ یا ‎+98 912 123 4567‎).";

    public static IRuleBuilderOptions<T, string?> ValidUsername<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(Username.IsValidNationalCode).WithMessage(UsernameMessage);

    /// <summary>data:image/(png|jpeg|webp|gif);base64,... or an https:// address. Never svg, html or another scheme.</summary>
    private static readonly System.Text.RegularExpressions.Regex AvatarPattern = new(
        @"^(data:image/(png|jpe?g|webp|gif);base64,[A-Za-z0-9+/=]+|https://[^\s<>""']+)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static IRuleBuilderOptions<T, string?> ValidAvatar<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => string.IsNullOrWhiteSpace(value) || AvatarPattern.IsMatch(value.Trim()))
            .WithMessage("تصویر پروفایل باید PNG، JPEG، WebP یا GIF باشد.");

    public static IRuleBuilderOptions<T, string?> ValidPhoneNumber<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(PhoneNumber.IsValid).WithMessage(PhoneMessage);
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Address).MaximumLength(1000);
        RuleFor(x => x.FatherName).MaximumLength(80);
        RuleFor(x => x.BirthDate).Must(date => date is null || date <= DateOnly.FromDateTime(DateTime.UtcNow)).WithMessage("تاریخ تولد نمی‌تواند در آینده باشد.");
        RuleFor(x => x.AvatarUrl).MaximumLength(ProfileRules.MaxAvatarLength).ValidAvatar()
            .Must(ProfileRules.IsPersonnelPhotoWithinLimit).WithMessage("عکس پرسنلی باید فایل تصویر با حداکثر حجم ۳۰۰ کیلوبایت باشد.");

        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Username).NotEmpty().ValidUsername();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.PhoneNumber).NotEmpty().ValidPhoneNumber();
        RuleFor(x => x.Password).NotEmpty().StrongPassword();
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Address).MaximumLength(1000);
        RuleFor(x => x.FatherName).MaximumLength(80);
        RuleFor(x => x.BirthDate).Must(date => date is null || date <= DateOnly.FromDateTime(DateTime.UtcNow)).WithMessage("تاریخ تولد نمی‌تواند در آینده باشد.");
        RuleFor(x => x.AvatarUrl).MaximumLength(ProfileRules.MaxAvatarLength).ValidAvatar()
            .Must(ProfileRules.IsPersonnelPhotoWithinLimit).WithMessage("عکس پرسنلی باید فایل تصویر با حداکثر حجم ۳۰۰ کیلوبایت باشد.");

        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Password).StrongPassword().When(x => !string.IsNullOrWhiteSpace(x.Password));
        // Null keeps the current username. A changed one must be a national code - checked by
        // the service, which knows the current value (older usernames stay valid unchanged).
        RuleFor(x => x.Username).NotEmpty().When(x => x.Username is not null);
        RuleFor(x => x.FirstName).MaximumLength(80);
        RuleFor(x => x.LastName).MaximumLength(80);
        RuleFor(x => x.PhoneNumber).ValidPhoneNumber().When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    }
}

public sealed class ChangeMyPasswordRequestValidator : AbstractValidator<ChangeMyPasswordRequest>
{
    public ChangeMyPasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEmpty().StrongPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("رمز عبور جدید باید با رمز فعلی متفاوت باشد.");
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200).WithMessage("مهلت تعیین رمز عبور جدید به پایان رسیده است. لطفاً دوباره کد بازیابی دریافت کنید.");
        RuleFor(x => x.NewPassword).NotEmpty().WithMessage("رمز عبور جدید را وارد کنید.")
            .StrongPassword();
    }
}

public sealed class UpdateMyProfileRequestValidator : AbstractValidator<UpdateMyProfileRequest>
{
    public UpdateMyProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(160);
        RuleFor(x => x.FirstName).MaximumLength(80);
        RuleFor(x => x.LastName).MaximumLength(80);
        RuleFor(x => x.PhoneNumber).ValidPhoneNumber().When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.AvatarUrl).MaximumLength(ProfileRules.MaxAvatarLength)
            .WithMessage("حجم تصویر پروفایل بیش از حد مجاز است.");
        RuleFor(x => x.AvatarUrl).ValidAvatar();
    }
}

public sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}

public sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Slug).NotEmpty().Matches("^[a-z0-9-]+$").MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}
