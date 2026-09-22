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

    public const string UsernameMessage = "Username must be a national code: exactly 10 digits.";
    public const string PhoneMessage = "Enter a valid mobile number (for example 09121234567 or +98 912 123 4567).";

    public static IRuleBuilderOptions<T, string?> ValidUsername<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(Username.IsValid).WithMessage(UsernameMessage);

    public static IRuleBuilderOptions<T, string?> ValidPhoneNumber<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(PhoneNumber.IsValid).WithMessage(PhoneMessage);
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Username).NotEmpty().ValidUsername();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.PhoneNumber).NotEmpty().ValidPhoneNumber();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Password).MinimumLength(8).MaximumLength(128).When(x => !string.IsNullOrWhiteSpace(x.Password));
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
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128)
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must differ from the current one.");
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
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
            .WithMessage("The avatar image is too large.");
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
