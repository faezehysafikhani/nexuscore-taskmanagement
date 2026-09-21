using FluentValidation;
using NexusCore.Application.Identity.Dtos;

namespace NexusCore.Application.Identity.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Email, username or mobile number - the repository decides which.
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

/// <summary>Shared rules for the optional profile fields.</summary>
internal static class ProfileRules
{
    // Starts with a letter so a username can never be mistaken for an email or phone number
    // at sign-in.
    public const string UsernamePattern = @"^[A-Za-z][A-Za-z0-9_.-]{2,63}$";
    public const string PhonePattern = @"^\+?[0-9]{7,15}$";
    public const int MaxAvatarLength = 1_500_000;
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.Username).Matches(ProfileRules.UsernamePattern).When(x => !string.IsNullOrWhiteSpace(x.Username))
            .WithMessage("Username must start with a letter and use 3-64 letters, digits, '_', '.' or '-'.");
        RuleFor(x => x.PhoneNumber).Matches(ProfileRules.PhonePattern).When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("Mobile number must be 7-15 digits, optionally starting with '+'.");
        RuleFor(x => x.TelegramChatId).MaximumLength(64);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Password).MinimumLength(8).MaximumLength(128).When(x => !string.IsNullOrWhiteSpace(x.Password));
        RuleFor(x => x.Username).Matches(ProfileRules.UsernamePattern).When(x => !string.IsNullOrWhiteSpace(x.Username))
            .WithMessage("Username must start with a letter and use 3-64 letters, digits, '_', '.' or '-'.");
        RuleFor(x => x.PhoneNumber).Matches(ProfileRules.PhonePattern).When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("Mobile number must be 7-15 digits, optionally starting with '+'.");
        RuleFor(x => x.TelegramChatId).MaximumLength(64);
    }
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Username).Matches(ProfileRules.UsernamePattern).When(x => !string.IsNullOrWhiteSpace(x.Username))
            .WithMessage("Username must start with a letter and use 3-64 letters, digits, '_', '.' or '-'.");
        RuleFor(x => x.PhoneNumber).Matches(ProfileRules.PhonePattern).When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("Mobile number must be 7-15 digits, optionally starting with '+'.");
        RuleFor(x => x.TelegramChatId).MaximumLength(64);
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
        RuleFor(x => x.Username).Matches(ProfileRules.UsernamePattern).When(x => !string.IsNullOrWhiteSpace(x.Username))
            .WithMessage("Username must start with a letter and use 3-64 letters, digits, '_', '.' or '-'.");
        RuleFor(x => x.PhoneNumber).Matches(ProfileRules.PhonePattern).When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("Mobile number must be 7-15 digits, optionally starting with '+'.");
        RuleFor(x => x.TelegramChatId).MaximumLength(64);
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
