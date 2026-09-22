using System.Security.Cryptography;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusCore.Application.Common;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Identity.Security;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Services;

public interface IAccountService
{
    Task<Result<ForgotPasswordResponse>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
    Task<Result<UserDto>> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken);
    Task<Result<UserDto>> UpdateMyPreferencesAsync(UpdateMyPreferencesRequest request, CancellationToken cancellationToken);
    Task<Result> ChangeMyPasswordAsync(ChangeMyPasswordRequest request, CancellationToken cancellationToken);
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
    IPasswordResetLinkSender resetLinkSender,
    IValidator<ResetPasswordRequest> resetValidator,
    IValidator<UpdateMyProfileRequest> profileValidator,
    IValidator<ChangeMyPasswordRequest> changePasswordValidator,
    ILogger<AccountService> logger) : IAccountService
{
    public async Task<Result<ForgotPasswordResponse>> ForgotPasswordAsync(
        ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Identifier))
        {
            return Result.Failure<ForgotPasswordResponse>(Error.Validation("A username or mobile number is required."));
        }

        var throttled = await loginProtection.ThrottleAsync(AuthAction.ForgotPassword, cancellationToken);
        if (throttled.IsFailure)
        {
            return Result.Failure<ForgotPasswordResponse>(throttled.Error);
        }

        // Decided before any lookup, so this answer cannot depend on whether the account exists.
        if (!resetLinkSender.IsAvailable && !resetLinkSender.ExposeTokenInResponse)
        {
            return Result.Failure<ForgotPasswordResponse>(Error.Validation(
                "Password reset by email is not available: email delivery is not configured on the server."));
        }

        // Same answer whether or not the account exists, so the endpoint cannot be used to find
        // out which accounts exist.
        const string message = "If an account matches and has an email address, a password reset link has been sent to it.";

        var user = await repository.FindUserByLoginAsync(request.Identifier, request.TenantSlug, cancellationToken);
        // No email address means nowhere to send the link; the answer stays the same.
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.Email))
        {
            return Result.Success(new ForgotPasswordResponse(message, null, null));
        }

        var now = DateTimeOffset.UtcNow;
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAtUtc = now.Add(resetLinkSender.TokenLifetime);

        // A new request supersedes every older, unused link.
        await repository.InvalidatePasswordResetTokensAsync(user.Id, now, cancellationToken);
        await repository.AddPasswordResetTokenAsync(
            new PasswordResetToken(Guid.NewGuid(), user.Id, passwordHasher.HashToken(rawToken), expiresAtUtc, currentUser.IpAddress),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var delivered = await resetLinkSender.SendAsync(user, rawToken, expiresAtUtc, cancellationToken);
        if (delivered.IsFailure && !resetLinkSender.ExposeTokenInResponse)
        {
            // A link that was never delivered must not stay usable.
            await repository.InvalidatePasswordResetTokensAsync(user.Id, DateTimeOffset.UtcNow, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Password reset link for user {UserId} could not be delivered.", user.Id);
            return Result.Failure<ForgotPasswordResponse>(delivered.Error);
        }

        await platformService.AuditAsync("users.password_reset_requested", nameof(User), user.Id.ToString(), null, cancellationToken);

        // Development only (PasswordReset:ReturnTokenInResponse): lets a client finish the flow
        // without a mail server.
        return Result.Success(resetLinkSender.ExposeTokenInResponse
            ? new ForgotPasswordResponse(message, rawToken, expiresAtUtc)
            : new ForgotPasswordResponse(message, null, null));
    }

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
            return Result.Failure(Error.Validation("This reset link is invalid or has expired. Request a new one."));
        }

        var now = DateTimeOffset.UtcNow;
        token.User.ChangePassword(passwordHasher.HashPassword(request.NewPassword));
        token.MarkUsed(now);
        await repository.InvalidatePasswordResetTokensAsync(token.UserId, now, cancellationToken);
        // The new password signs the user out of every existing session.
        await repository.RevokeRefreshTokensAsync(token.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("users.password_reset", nameof(User), token.UserId.ToString(), null, cancellationToken);
        return Result.Success();
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
            return Result.Failure<UserDto>(Error.NotFound("User was not found."));
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber)
            && await repository.PhoneNumberExistsAsync(user.TenantId, request.PhoneNumber, user.Id, cancellationToken))
        {
            return Result.Failure<UserDto>(Error.Conflict("This mobile number cannot be used. Enter another one."));
        }

        // The username (national code) is identity data: only an administrator changes it.
        if (!string.IsNullOrWhiteSpace(request.FirstName) || !string.IsNullOrWhiteSpace(request.LastName))
        {
            user.SetName(request.FirstName ?? string.Empty, request.LastName ?? string.Empty);
        }
        else
        {
            user.ChangeDisplayName(request.DisplayName);
        }

        user.UpdateContactDetails(user.Username, request.PhoneNumber, request.NotifySms);
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
            return Result.Failure<UserDto>(Error.NotFound("User was not found."));
        }

        user.SetPreferences(Limit(request.Theme, 40), Limit(request.ColorPalette, 40), Limit(request.ThemeMode, 10));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(IdentityMappings.ToUserDto(user));
    }

    /// <summary>
    /// The signed-in user changes their own password, proving the current one. This is also how
    /// the built-in system administrator changes theirs - nobody else can.
    /// </summary>
    public async Task<Result> ChangeMyPasswordAsync(ChangeMyPasswordRequest request, CancellationToken cancellationToken)
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
            return Result.Failure(Error.NotFound("User was not found."));
        }

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure(Error.Validation("The current password is not correct."));
        }

        user.ChangePassword(passwordHasher.HashPassword(request.NewPassword));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("users.change_own_password", nameof(User), user.Id.ToString(), null, cancellationToken);
        return Result.Success();
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}
