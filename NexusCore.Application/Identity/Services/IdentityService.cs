using System.Security.Cryptography;
using FluentValidation;
using Microsoft.Extensions.Options;
using NexusCore.Application.Common;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Identity.Security;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Services;

public sealed class IdentityService(
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IUnitOfWork unitOfWork,
    IPlatformService platformService,
    IValidator<LoginRequest> loginValidator,
    IValidator<CreateUserRequest> createUserValidator,
    IValidator<UpdateUserRequest> updateUserValidator,
    IValidator<CreateRoleRequest> createRoleValidator,
    IValidator<CreateTenantRequest> createTenantValidator,
    ILoginProtection loginProtection,
    AuthSessionIssuer sessionIssuer,
    ICurrentUserContext currentUser,
    IUserGroupPermissionProvider groupPermissions,
    IOptions<ManagedPermissionOptions> managedPermissionOptions,
    IEnumerable<IUserDeactivationListener> deactivationListeners,
    IUserReachScope? reachScope = null,
    IOptions<SignInSessionOptions>? sessionOptions = null) : IIdentityService
{
    private readonly ManagedPermissionOptions _managed = managedPermissionOptions.Value;
    private readonly SignInSessionOptions _session = sessionOptions?.Value ?? new SignInSessionOptions();

    // A real hash that matches no password. An unknown account is checked against it, so that
    // sign-in takes as long for an account that does not exist as for one that does.
    private static string? _unknownAccountHash;

    // One message for every way sign-in can fail on the credentials, so the answer never tells
    // whether a username or mobile number is registered.
    private const string InvalidCredentials = "نام کاربری، شماره تلفن یا رمز عبور صحیح نیست.";

    /// <summary>Sign-in with the right password to a disabled account (HTTP 403).</summary>
    public static readonly Error AccountDisabled = new("account.disabled", "حساب کاربری شما غیرفعال شده است. لطفاً با مدیر سامانه تماس بگیرید.");

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var validation = await loginValidator.ValidateAsResultAsync(request, cancellationToken);
        if (validation.IsFailure)
        {
            return Result.Failure<AuthResponse>(validation.Error);
        }

        // Limits and CAPTCHA come first: while a CAPTCHA is due, the password is not even
        // checked, so guessing cannot continue without solving one each time.
        var guard = await loginProtection.BeforeLoginAttemptAsync(
            request.Identifier, request.CaptchaId, request.CaptchaAnswer, cancellationToken);
        if (guard.IsFailure)
        {
            // A missing CAPTCHA is the normal next step, not an event; the rest is recorded.
            if (guard.Error.Code != "captcha.required")
            {
                var action = guard.Error.Code == "too_many_requests" ? "identity.login_blocked" : "identity.login_captcha_failed";
                await platformService.AuditForAsync(null, null, action, nameof(User), null, AuditIdentifier(request.Identifier), cancellationToken);
            }

            return Result.Failure<AuthResponse>(guard.Error);
        }

        // The identifier is a username or a mobile number; an email address matches nothing.
        var user = await repository.FindUserByLoginAsync(request.Identifier, request.TenantSlug, cancellationToken);
        _unknownAccountHash ??= passwordHasher.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        var passwordChecked = passwordHasher.Verify(request.Password, user?.PasswordHash ?? _unknownAccountHash);
        var passwordOk = user is not null && passwordChecked;
        if (user is null || !user.IsActive || !passwordOk)
        {
            var captchaNext = await loginProtection.RecordFailedLoginAsync(request.Identifier, cancellationToken);
            // The reason is for administrators only.
            var reason = user is null ? "unknown account" : !passwordOk ? "wrong password" : "account disabled";
            await platformService.AuditForAsync(user?.TenantId, user?.Id, "identity.login_failed", nameof(User), user?.Id.ToString(), $"{AuditIdentifier(request.Identifier)} ({reason})", cancellationToken);

            // Only someone who knows the password learns that the account is disabled; a wrong
            // password gets the same answer as an unknown account, so the status stays hidden.
            if (passwordOk)
            {
                return Result.Failure<AuthResponse>(AccountDisabled);
            }

            return Result.Failure<AuthResponse>(captchaNext
                ? new Error("unauthorized.captcha_required", InvalidCredentials)
                : Error.Unauthorized(InvalidCredentials));
        }

        await loginProtection.RecordSuccessfulLoginAsync(request.Identifier, cancellationToken);
        return Result.Success(await sessionIssuer.IssueAsync(user, cancellationToken));
    }

    public async Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result.Failure<AuthResponse>(Error.Unauthorized("نشست کاربری معتبر نیست؛ دوباره وارد شوید."));
        }

        var invalid = Result.Failure<AuthResponse>(Error.Unauthorized("نشست کاربری معتبر نیست؛ دوباره وارد شوید."));
        var refreshToken = await repository.FindRefreshTokenAsync(passwordHasher.HashToken(request.RefreshToken), cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (refreshToken?.User is null || !refreshToken.User.IsActive || refreshToken.ExpiresAtUtc <= now)
        {
            return invalid;
        }

        // Normal case: the token has not been used. The database lets one request take it; a
        // second one with the same token (another tab, a retry) loses the race and is handled
        // like a token that was exchanged a moment ago.
        var nextRefreshToken = jwtTokenService.CreateRefreshToken();
        var usedAt = refreshToken.UsedAtUtc;
        var exchanged = refreshToken.ReplacedByTokenHash is not null;
        if (refreshToken.RevokedAtUtc is null)
        {
            if (await repository.TryUseRefreshTokenAsync(refreshToken.Id, passwordHasher.HashToken(nextRefreshToken), now, cancellationToken))
            {
                return Result.Success(await IssueSuccessorAsync(refreshToken, nextRefreshToken, cancellationToken));
            }

            var state = await repository.GetRefreshTokenStateAsync(refreshToken.Id, cancellationToken);
            usedAt = state?.UsedAtUtc;
            exchanged = state?.ReplacedByTokenHash is not null;
        }

        // Revoked by sign-out, a password change or the administrator: just not valid any more.
        if (!exchanged)
        {
            return invalid;
        }

        // Exchanged very recently: a duplicate request, not an attack. It gets its own token
        // from the same sign-in.
        if (usedAt is { } used && now - used <= TimeSpan.FromSeconds(Math.Max(0, _session.ReuseGraceSeconds)))
        {
            return Result.Success(await IssueSuccessorAsync(refreshToken, nextRefreshToken, cancellationToken));
        }

        // Exchanged earlier and presented again: whoever holds this copy is not the one who got
        // the new token. The whole sign-in ends, for the user and for the thief alike.
        await repository.RevokeRefreshTokenFamilyAsync(refreshToken.UserId, refreshToken.Family, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditForAsync(refreshToken.User.TenantId, refreshToken.UserId, "identity.refresh_token_reuse", nameof(User), refreshToken.UserId.ToString(), null, cancellationToken);
        return invalid;
    }

    /// <summary>A new refresh token of the same sign-in: same family, same last day.</summary>
    private async Task<AuthResponse> IssueSuccessorAsync(RefreshToken previous, string rawRefreshToken, CancellationToken cancellationToken)
    {
        var user = previous.User!;
        var entity = user.AddRefreshToken(passwordHasher.HashToken(rawRefreshToken), previous.ExpiresAtUtc, currentUser.IpAddress, previous.Family);
        await repository.AddRefreshTokenAsync(entity, cancellationToken);

        var permissions = await repository.GetUserPermissionNamesAsync(user.Id, cancellationToken);
        var accessToken = jwtTokenService.CreateAccessToken(user, permissions);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResponse(accessToken.Token, rawRefreshToken, accessToken.ExpiresAtUtc, ToUserDto(user), entity.ExpiresAtUtc);
    }

    /// <summary>
    /// Ends the sign-in the refresh token belongs to (the token and its whole family). The same
    /// answer whether or not the token was still valid, so the call reveals nothing about it.
    /// </summary>
    public async Task<Result> LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result.Success();
        }

        var refreshToken = await repository.FindRefreshTokenAsync(passwordHasher.HashToken(request.RefreshToken), cancellationToken);
        if (refreshToken is null)
        {
            return Result.Success();
        }

        refreshToken.Revoke(null);
        await repository.RevokeRefreshTokenFamilyAsync(refreshToken.UserId, refreshToken.Family, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditForAsync(refreshToken.User?.TenantId, refreshToken.UserId, "identity.logout", nameof(User), refreshToken.UserId.ToString(), null, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<CurrentUserResponse>(Error.NotFound("کاربر یافت نشد."));
        }

        var permissions = await repository.GetUserPermissionNamesAsync(user.Id, cancellationToken);
        return Result.Success(new CurrentUserResponse(ToUserDto(user), permissions));
    }

    public async Task<Result<PagedResult<UserDto>>> ListUsersAsync(Guid? tenantId, int? pageNumber, int? pageSize, string? search, CancellationToken cancellationToken)
    {
        // Kept to one's own company (see IUserReachScope): other companies' people are not listed.
        // Those with no place yet still are, so that whoever may place them finds them.
        var resolvedTenant = currentUser.ResolveTenant(tenantId);
        var reach = reachScope is not null && resolvedTenant is { } reachTenant && currentUser.UserId is { } me
            ? await reachScope.GetReachAsync(reachTenant, me, cancellationToken)
            : null;
        var users = await repository.ListUsersAsync(
    resolvedTenant,
    pageNumber,
    pageSize,
    search,
    cancellationToken,
    reach?.PlacedElsewhere.ToList());

        return Result.Success(new PagedResult<UserDto>(
            users.Items.Select(ToUserDto).ToList(),
            users.PageNumber,
            users.PageSize,
            users.TotalCount));
    }

    public async Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var validation = await createUserValidator.ValidateAsResultAsync(request, cancellationToken);
        if (validation.IsFailure)
        {
            return Result.Failure<UserDto>(validation.Error);
        }

        if (!currentUser.CanAccessTenant(request.TenantId)
            || await repository.GetTenantByIdAsync(request.TenantId, cancellationToken) is null)
        {
            return Result.Failure<UserDto>(Error.NotFound("سازمان یافت نشد."));
        }

        var taken = await FindTakenSignInDetailAsync(request.TenantId, null, request.Username, request.PhoneNumber, request.Email, cancellationToken);
        if (taken is not null)
        {
            return Result.Failure<UserDto>(taken);
        }

        // Never a system account: the built-in administrator only comes from the seeder.
        var user = new User(Guid.NewGuid(), request.TenantId, request.Email, $"{request.FirstName} {request.LastName}", passwordHasher.HashPassword(request.Password), request.IsActive);
        user.SetName(request.FirstName, request.LastName);
        user.UpdateContactDetails(request.Username, request.PhoneNumber, request.NotifySms);
        user.SetPersonalDetails(request.Address, request.FatherName, request.BirthDate);
        user.SetAvatar(request.AvatarUrl);
        await repository.AddUserAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("users.create", nameof(User), user.Id.ToString(), user.Username, cancellationToken);

        return Result.Success(ToUserDto(user));
    }

    public async Task<Result<UserDto>> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var validation = await updateUserValidator.ValidateAsResultAsync(request, cancellationToken);
        if (validation.IsFailure)
        {
            return Result.Failure<UserDto>(validation.Error);
        }

        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        // Another organization's user does not exist for this caller.
        if (user is null || !currentUser.CanAccessTenant(user.TenantId))
        {
            return Result.Failure<UserDto>(Error.NotFound("کاربر یافت نشد."));
        }

        var usernameChanges = request.Username is not null && !string.Equals(request.Username.Trim(), user.Username, StringComparison.OrdinalIgnoreCase);
        if (user.IsSystem)
        {
            // Nobody changes the built-in administrator's identity, status or password through
            // user administration. It changes its own password itself (auth/me/password).
            var changesIdentity =
                usernameChanges
                || (request.Email is not null && !string.Equals(request.Email.Trim(), user.Email ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                || (request.PhoneNumber is not null && PhoneNumber.Normalize(request.PhoneNumber) != user.PhoneNumber && request.PhoneNumber.Trim() != (user.PhoneNumber ?? string.Empty))
                || (request.FirstName is not null && request.FirstName.Trim() != (user.FirstName ?? string.Empty))
                || (request.LastName is not null && request.LastName.Trim() != (user.LastName ?? string.Empty))
                || request.DisplayName.Trim() != user.DisplayName
                || !request.IsActive
                || !string.IsNullOrWhiteSpace(request.Password);
            if (changesIdentity)
            {
                return Result.Failure<UserDto>(Error.Forbidden("مشخصات مدیر اصلی سامانه از این بخش قابل تغییر نیست. رمز عبور آن فقط از پروفایل خودش تغییر می‌کند."));
            }
        }

        if (usernameChanges && !Username.IsValidNationalCode(request.Username!.Trim()))
        {
            return Result.Failure<UserDto>(Error.Validation("نام کاربری باید کد ملی معتبر ۱۰ رقمی باشد."));
        }

        if (currentUser.UserId == user.Id && !request.IsActive)
        {
            return Result.Failure<UserDto>(Error.Validation("نمی‌توانید حساب کاربری خودتان را غیرفعال کنید."));
        }

        // Enabling or disabling is users.change_status, also when it comes in with an edit.
        var statusChanges = request.IsActive != user.IsActive;
        if (statusChanges && currentUser.UserId is { } callerId
            && !(await repository.GetUserPermissionNamesAsync(callerId, cancellationToken)).Contains(IdentityPermissions.UsersChangeStatus))
        {
            return Result.Failure<UserDto>(Error.Forbidden("برای فعال یا غیرفعال کردن کاربران، مجوز «تغییر وضعیت کاربران» لازم است."));
        }

        // Password, mobile number (which is how a password is recovered) and status of someone who
        // holds more than the caller are not the caller's to change.
        var phoneChanges = request.PhoneNumber is not null
            && (PhoneNumber.Normalize(request.PhoneNumber) ?? string.Empty) != (user.PhoneNumber ?? string.Empty);
        if (phoneChanges || statusChanges || !string.IsNullOrWhiteSpace(request.Password))
        {
            var outranked = await GuardAgainstHigherPrivilegeAsync(user, cancellationToken);
            if (outranked.IsFailure)
            {
                return Result.Failure<UserDto>(outranked.Error);
            }
        }

        var taken = await FindTakenSignInDetailAsync(user.TenantId, user.Id, usernameChanges ? request.Username : null, request.PhoneNumber, request.Email, cancellationToken);
        if (taken is not null)
        {
            return Result.Failure<UserDto>(taken);
        }

        user.SetPersonalDetails(request.Address ?? user.Address, request.FatherName ?? user.FatherName, request.ClearBirthDate ? null : request.BirthDate ?? user.BirthDate);
        if (request.AvatarUrl is not null) user.SetAvatar(request.AvatarUrl);
        var previousPhone = user.PhoneNumber;
        user.UpdateProfile(request.DisplayName, request.IsActive);
        if (request.FirstName is not null || request.LastName is not null)
        {
            user.SetName(request.FirstName ?? user.FirstName ?? string.Empty, request.LastName ?? user.LastName ?? string.Empty);
        }

        // Null keeps the current value; an empty string clears email or mobile number.
        if (request.Email is not null)
        {
            user.ChangeEmail(request.Email);
        }

        user.UpdateContactDetails(
            usernameChanges ? request.Username!.Trim() : user.Username,
            request.PhoneNumber ?? user.PhoneNumber,
            request.NotifySms ?? user.NotifySms);

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.ChangePassword(passwordHasher.HashPassword(request.Password));
            // A new password ends every existing session of that user.
            await repository.RevokeRefreshTokensAsync(user.Id, cancellationToken);
        }

        if (!user.IsActive)
        {
            await repository.RevokeRefreshTokensAsync(user.Id, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("users.update", nameof(User), user.Id.ToString(), user.Username, cancellationToken);
        if (phoneChanges)
        {
            // A mobile number is a way into the account (password recovery): its change is a
            // record of its own, with the numbers masked.
            await platformService.AuditAsync("users.change_phone", nameof(User), user.Id.ToString(),
                $"{MaskPhone(previousPhone)} -> {MaskPhone(user.PhoneNumber)}", cancellationToken);
        }

        if (statusChanges)
        {
            await platformService.AuditAsync(user.IsActive ? "users.enable" : "users.disable", nameof(User), user.Id.ToString(), user.Username, cancellationToken);
            if (!user.IsActive)
            {
                await TellDeactivatedAsync(user, cancellationToken);
            }
        }

        return Result.Success(ToUserDto(user));
    }

    public async Task<Result<UserDto>> SetUserStatusAsync(Guid userId, SetUserStatusRequest request, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        // Another organization's user does not exist for this caller.
        if (user is null || !currentUser.CanAccessTenant(user.TenantId))
        {
            return Result.Failure<UserDto>(Error.NotFound("کاربر یافت نشد."));
        }

        if (user.IsSystem && !request.IsActive)
        {
            return Result.Failure<UserDto>(Error.Forbidden("مدیر اصلی سامانه قابل غیرفعال‌سازی نیست."));
        }

        if (currentUser.UserId == user.Id && !request.IsActive)
        {
            return Result.Failure<UserDto>(Error.Validation("نمی‌توانید حساب کاربری خودتان را غیرفعال کنید."));
        }

        var outranked = await GuardAgainstHigherPrivilegeAsync(user, cancellationToken);
        if (outranked.IsFailure)
        {
            return Result.Failure<UserDto>(outranked.Error);
        }

        var wasActive = user.IsActive;
        user.SetActive(request.IsActive);
        if (!request.IsActive)
        {
            // A disabled account keeps no session: its refresh tokens stop working at once.
            await repository.RevokeRefreshTokensAsync(user.Id, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync(request.IsActive ? "users.enable" : "users.disable", nameof(User), user.Id.ToString(), user.Username, cancellationToken);
        if (wasActive && !request.IsActive)
        {
            await TellDeactivatedAsync(user, cancellationToken);
        }

        return Result.Success(ToUserDto(user));
    }

    /// <summary>The optional modules release what a disabled person held (their posts...).</summary>
    private async Task TellDeactivatedAsync(User user, CancellationToken cancellationToken)
    {
        foreach (var listener in deactivationListeners)
        {
            await listener.UserDeactivatedAsync(user.TenantId, user.Id, cancellationToken);
        }
    }

    public async Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        // Another organization's user does not exist for this caller.
        if (user is null || !currentUser.CanAccessTenant(user.TenantId))
        {
            return Result.Failure(Error.NotFound("کاربر یافت نشد."));
        }

        if (user.IsSystem)
        {
            return Result.Failure(Error.Forbidden("مدیر اصلی سامانه قابل حذف نیست."));
        }

        if (currentUser.UserId == user.Id)
        {
            return Result.Failure(Error.Validation("نمی‌توانید حساب کاربری خودتان را حذف کنید."));
        }

        var outranked = await GuardAgainstHigherPrivilegeAsync(user, cancellationToken);
        if (outranked.IsFailure)
        {
            return outranked;
        }

        var auditDetails = user.Username ?? user.Email;
        await repository.RemoveUserAsync(user, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Other data (tasks they own, a team of theirs assigned to tasks) still points at the
            // user. That is a conflict to resolve first, not a server error.
            return Result.Failure(Error.Conflict("این کاربر هنوز به اطلاعات دیگری (مثلاً فعالیت‌هایش) متصل است. به‌جای حذف، حساب را غیرفعال کنید یا ابتدا آن اطلاعات را واگذار کنید."));
        }
        await platformService.AuditAsync("users.delete", nameof(User), user.Id.ToString(), auditDetails, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AssignRolesAsync(Guid userId, AssignUserRolesRequest request, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        // Another organization's user does not exist for this caller.
        if (user is null || !currentUser.CanAccessTenant(user.TenantId))
        {
            return Result.Failure(Error.NotFound("کاربر یافت نشد."));
        }

        var guard = GuardAccessChange(user);
        if (guard.IsFailure)
        {
            return guard;
        }

        if (request.RoleIds is null)
        {
            return Result.Failure(Error.Validation("فهرست نقش‌ها را بفرستید."));
        }

        var requestedRoles = request.RoleIds.Distinct().ToList();
        var tenantRoles = (await repository.ListRolesAsync(user.TenantId, cancellationToken)).ToDictionary(role => role.Id);
        if (requestedRoles.Any(id => !tenantRoles.ContainsKey(id)))
        {
            return Result.Failure(Error.Validation("یک یا چند نقش در سازمان این کاربر وجود ندارد."));
        }

        var currentRoles = user.Roles.Select(role => role.RoleId).ToHashSet();
        var notHeld = requestedRoles
            .Where(id => !currentRoles.Contains(id))
            .SelectMany(id => tenantRoles[id].Permissions.Select(grant => grant.Permission?.Name ?? string.Empty))
            .Where(name => name.Length > 0 && !CallerHolds(name))
            .Distinct()
            .ToList();
        if (notHeld.Count > 0)
        {
            return Result.Failure(Error.Forbidden("You can only assign roles whose permissions you have yourself: " + string.Join(", ", notHeld)));
        }

        user.SetRoles(requestedRoles);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("users.assign_roles", nameof(User), user.Id.ToString(), string.Join(",", request.RoleIds), cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// What the user may do and why: every permission this product manages, with whether it comes
    /// from the user directly, from a role or from an organisational group, whether it is denied
    /// to the user, and whether the user actually has it. On the user, a permission can be granted
    /// directly or denied (which overrides roles and groups); roles and groups are changed on
    /// themselves.
    /// </summary>
    public async Task<Result<UserPermissionsDto>> GetUserAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        // Another organization's user does not exist for this caller.
        if (user is null || !currentUser.CanAccessTenant(user.TenantId))
        {
            return Result.Failure<UserPermissionsDto>(Error.NotFound("کاربر یافت نشد."));
        }

        var permissions = _managed.Managed(await repository.ListPermissionsAsync(cancellationToken));
        var roles = await repository.ListRolesAsync(user.TenantId, cancellationToken);
        var userRoleIds = user.Roles.Select(role => role.RoleId).ToHashSet();
        var grantingRoles = roles
            .Where(role => userRoleIds.Contains(role.Id))
            .SelectMany(role => role.Permissions.Select(grant => (grant.PermissionId, role.Name)))
            .GroupBy(pair => pair.PermissionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(pair => pair.Name).Distinct().ToList());
        var grantingGroups = await groupPermissions.GetGrantingGroupsAsync(user.Id, cancellationToken);
        var direct = user.Permissions.Where(entry => !entry.IsDenied).Select(entry => entry.PermissionId).ToHashSet();
        var denied = user.Permissions.Where(entry => entry.IsDenied).Select(entry => entry.PermissionId).ToHashSet();
        // Exactly what the authorization checks use, so the page cannot disagree with the server.
        var effective = (await repository.GetUserPermissionNamesAsync(user.Id, cancellationToken)).ToHashSet(StringComparer.Ordinal);

        var entries = permissions
            .OrderBy(permission => permission.Module).ThenBy(permission => permission.Name)
            .Select(permission =>
            {
                var byRole = grantingRoles.GetValueOrDefault(permission.Id, []);
                var byGroup = grantingGroups.GetValueOrDefault(permission.Id, []);
                var isDirect = direct.Contains(permission.Id);
                var isEffective = effective.Contains(permission.Name);
                return new UserPermissionEntryDto(
                    permission.Id, permission.Name, permission.Module, permission.Description,
                    isDirect, byRole.Count > 0, byRole, byGroup.Count > 0, byGroup,
                    DeniedForUser: denied.Contains(permission.Id),
                    Effective: isEffective,
                    GrantedAsPrerequisite: isEffective && !isDirect && byRole.Count == 0 && byGroup.Count == 0);
            })
            .ToList();

        return Result.Success(new UserPermissionsDto(
            user.Id, user.DisplayName, user.Email,
            user.Roles.Select(role => role.Role?.Name ?? role.RoleId.ToString()).ToList(),
            entries));
    }

    /// <summary>
    /// Replaces the permissions granted directly to the user and, when the request carries them,
    /// the user's explicit denials. A caller can only hand out permissions they hold themselves -
    /// and lifting a denial hands the permission back, so the same rule applies to denials - so
    /// this cannot be used to escalate. Only this product's managed permissions can be set; the
    /// user's grants and denials of other modules are kept as they are.
    /// </summary>
    public async Task<Result> SetUserDirectPermissionsAsync(Guid userId, AssignUserPermissionsRequest request, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        // Another organization's user does not exist for this caller.
        if (user is null || !currentUser.CanAccessTenant(user.TenantId))
        {
            return Result.Failure(Error.NotFound("کاربر یافت نشد."));
        }

        var guard = GuardAccessChange(user);
        if (guard.IsFailure)
        {
            return guard;
        }

        if (request.PermissionIds is null)
        {
            return Result.Failure(Error.Validation("فهرست مجوزها را بفرستید."));
        }

        var requested = request.PermissionIds.Distinct().ToList();
        var requestedDenied = request.DeniedPermissionIds?.Distinct().ToList();
        if (requestedDenied is not null && requestedDenied.Intersect(requested).Any())
        {
            return Result.Failure(Error.Validation("یک مجوز را نمی‌شود هم‌زمان به یک کاربر داد و از او گرفت."));
        }

        var named = requested.Concat(requestedDenied ?? []).Distinct().ToList();
        var currentGranted = user.Permissions.Where(entry => !entry.IsDenied).Select(entry => entry.PermissionId).ToHashSet();
        var currentDenied = user.Permissions.Where(entry => entry.IsDenied).Select(entry => entry.PermissionId).ToHashSet();
        var byId = (await repository.GetPermissionsByIdsAsync(named.Concat(currentGranted).Concat(currentDenied).Distinct().ToList(), cancellationToken))
            .ToDictionary(permission => permission.Id);
        if (named.Any(id => !byId.ContainsKey(id)))
        {
            return Result.Failure(Error.Validation("یک یا چند مجوز انتخاب‌شده وجود ندارد."));
        }

        var unmanaged = named.Select(id => byId[id]).Where(permission => !_managed.IsManaged(permission)).Select(permission => permission.Name).ToList();
        if (unmanaged.Count > 0)
        {
            return Result.Failure(Error.Validation("These permissions are not managed in this product: " + string.Join(", ", unmanaged)));
        }

        bool Managed(Guid id) => byId.TryGetValue(id, out var permission) && _managed.IsManaged(permission);

        // A permission is useless without the ones it depends on (PermissionPrerequisites), so they
        // are saved together with it - for roles, groups and users alike - and the screens show
        // them checked after a reload. Denying one that a granted permission needs is refused.
        var allPermissions = await repository.ListPermissionsAsync(cancellationToken);
        var nameMap = allPermissions.ToDictionary(p => p.Name, StringComparer.Ordinal);
        foreach (var impliedName in PermissionPrerequisites.ImpliedBy(requested.Select(id => byId[id].Name)))
        {
            if (!nameMap.TryGetValue(impliedName, out var implied) || !_managed.IsManaged(implied) || requested.Contains(implied.Id))
            {
                continue;
            }

            if (requestedDenied is not null && requestedDenied.Contains(implied.Id))
            {
                return Result.Failure(Error.Validation($"\"{impliedName}\" cannot be denied while a permission that requires it is granted."));
            }

            requested.Add(implied.Id);
            byId[implied.Id] = implied;
        }

        // Every change that gives something back needs the caller to hold it: a new grant, and a
        // denial that is added or lifted.
        var changed = requested.Where(id => !currentGranted.Contains(id))
            .Concat(requestedDenied is null ? [] : requestedDenied.Where(id => !currentDenied.Contains(id)))
            .Concat(requestedDenied is null ? [] : currentDenied.Where(id => Managed(id) && !requestedDenied.Contains(id)))
            .Distinct()
            .ToList();
        if (changed.Count > 0 && currentUser.UserId is { } callerId)
        {
            var callerPermissions = (await repository.GetUserPermissionNamesAsync(callerId, cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var notHeld = changed.Select(id => byId[id].Name).Where(name => !callerPermissions.Contains(name)).ToList();
            if (notHeld.Count > 0)
            {
                return Result.Failure(Error.Forbidden("You can only grant permissions you have yourself: " + string.Join(", ", notHeld)));
            }
        }

        // Grants and denials of modules this product does not manage stay as they were.
        user.SetDirectPermissions(
            requested.Concat(currentGranted.Where(id => !Managed(id))),
            requestedDenied?.Concat(currentDenied.Where(id => !Managed(id))));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var audit = string.Join(",", requested.Select(id => byId[id].Name));
        if (requestedDenied is not null)
        {
            audit += "; denied: " + string.Join(",", requestedDenied.Select(id => byId[id].Name));
        }

        await platformService.AuditAsync("users.assign_permissions", nameof(User), user.Id.ToString(), audit, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Changing someone's password or mobile number is taking over their account, and disabling or
    /// deleting them is acting on them. Like handing out roles and permissions, that is only
    /// allowed for a user who is not above the caller: the target must not hold a permission the
    /// caller lacks. Acting on oneself is not an escalation and is left to the other checks.
    /// </summary>
    private async Task<Result> GuardAgainstHigherPrivilegeAsync(User target, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId || callerId == target.Id)
        {
            return Result.Success();
        }

        var callerPermissions = (await repository.GetUserPermissionNamesAsync(callerId, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var higher = (await repository.GetUserPermissionNamesAsync(target.Id, cancellationToken))
            .Where(name => !callerPermissions.Contains(name))
            .ToList();
        return higher.Count == 0
            ? Result.Success()
            : Result.Failure(Error.Forbidden("This user holds permissions you do not have, so you cannot change their password, mobile number or status: " + string.Join(", ", higher.Take(5))));
    }

    private static string MaskPhone(string? phone) =>
        string.IsNullOrEmpty(phone) ? "-" : phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];

    /// <summary>
    /// Nobody changes the built-in administrator's access, and nobody changes their own - so
    /// no one can make themselves more powerful through the user screens.
    /// </summary>
    private Result GuardAccessChange(User target)
    {
        if (target.IsSystem)
        {
            return Result.Failure(Error.Forbidden("دسترسی مدیر اصلی سامانه قابل تغییر نیست."));
        }

        if (currentUser.UserId == target.Id)
        {
            return Result.Failure(Error.Forbidden("نمی‌توانید نقش‌ها یا مجوزهای خودتان را تغییر دهید."));
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<RoleDto>>> ListRolesAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var roles = await repository.ListRolesAsync(currentUser.ResolveTenant(tenantId), cancellationToken);
        return Result.Success<IReadOnlyList<RoleDto>>(roles.Select(ToRoleDto).ToList());
    }

    public async Task<Result<RoleDto>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var validation = await createRoleValidator.ValidateAsResultAsync(request, cancellationToken);
        if (validation.IsFailure)
        {
            return Result.Failure<RoleDto>(validation.Error);
        }

        if (!currentUser.CanAccessTenant(request.TenantId))
        {
            return Result.Failure<RoleDto>(Error.NotFound("سازمان یافت نشد."));
        }

        if (await repository.RoleNameExistsAsync(request.TenantId, request.Name, cancellationToken))
        {
            return Result.Failure<RoleDto>(Error.Conflict("نقشی با این نام قبلاً ثبت شده است."));
        }

        var role = new Role(Guid.NewGuid(), request.TenantId, request.Name, request.Description);
        await repository.AddRoleAsync(role, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("roles.create", nameof(Role), role.Id.ToString(), role.Name, cancellationToken);
        return Result.Success(ToRoleDto(role));
    }

    public async Task<Result<RoleDto>> UpdateRoleAsync(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await repository.GetRoleByIdAsync(roleId, cancellationToken);
        if (role is null || !currentUser.CanAccessTenant(role.TenantId))
        {
            return Result.Failure<RoleDto>(Error.NotFound("نقش یافت نشد."));
        }

        if (role.IsSystem)
        {
            return Result.Failure<RoleDto>(Error.Forbidden("نقش مدیر سامانه قابل تغییر نیست."));
        }

        role.Update(request.Name, request.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("roles.update", nameof(Role), role.Id.ToString(), role.Name, cancellationToken);
        return Result.Success(ToRoleDto(role));
    }

    public async Task<Result> AssignPermissionsAsync(Guid roleId, AssignRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        var role = await repository.GetRoleByIdAsync(roleId, cancellationToken);
        if (role is null || !currentUser.CanAccessTenant(role.TenantId))
        {
            return Result.Failure(Error.NotFound("نقش یافت نشد."));
        }

        // The Administrator role always carries every permission (the seeder restores it);
        // editing it could only lock administrators out.
        if (role.IsSystem)
        {
            return Result.Failure(Error.Forbidden("مجوزهای نقش مدیر سامانه قابل تغییر نیست."));
        }

        if (request.PermissionIds is null)
        {
            return Result.Failure(Error.Validation("فهرست مجوزها را بفرستید."));
        }

        var requested = request.PermissionIds.Distinct().ToList();
        var known = await repository.GetPermissionsByIdsAsync(requested, cancellationToken);
        if (known.Count != requested.Count)
        {
            return Result.Failure(Error.Validation("یک یا چند مجوز انتخاب‌شده وجود ندارد."));
        }

        var unmanaged = known.Where(permission => !_managed.IsManaged(permission)).Select(permission => permission.Name).ToList();
        if (unmanaged.Count > 0)
        {
            return Result.Failure(Error.Validation("These permissions are not managed in this product: " + string.Join(", ", unmanaged)));
        }

        // Prerequisites are saved with the permission that needs them (same rule as for users and groups).
        var allPermissionsForRoles = await repository.ListPermissionsAsync(cancellationToken);
        var nameMapForRoles = allPermissionsForRoles.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var addedForRole = false;
        foreach (var impliedName in PermissionPrerequisites.ImpliedBy(known.Select(p => p.Name)))
        {
            if (nameMapForRoles.TryGetValue(impliedName, out var implied) && _managed.IsManaged(implied) && !requested.Contains(implied.Id))
            {
                requested.Add(implied.Id);
                addedForRole = true;
            }
        }

        if (addedForRole)
        {
            known = await repository.GetPermissionsByIdsAsync(requested, cancellationToken);
        }

        var already = role.Permissions.Select(grant => grant.PermissionId).ToHashSet();
        var notHeld = known.Where(permission => !already.Contains(permission.Id) && !CallerHolds(permission.Name)).Select(permission => permission.Name).ToList();
        if (notHeld.Count > 0)
        {
            return Result.Failure(Error.Forbidden("You can only grant permissions you have yourself: " + string.Join(", ", notHeld)));
        }

        // The role's permissions of modules this product does not manage stay as they were.
        role.SetPermissions(requested.Concat(role.Permissions
            .Where(grant => grant.Permission is { } permission && !_managed.IsManaged(permission))
            .Select(grant => grant.PermissionId)).ToList());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("roles.assign_permissions", nameof(Role), role.Id.ToString(), string.Join(",", request.PermissionIds), cancellationToken);
        return Result.Success();
    }

    /// <summary>The permissions this product manages (all when it does not narrow them), by module.</summary>
    public async Task<Result<IReadOnlyList<PermissionGroupDto>>> ListPermissionsGroupedAsync(CancellationToken cancellationToken)
    {
        var permissions = _managed.Managed(await repository.ListPermissionsAsync(cancellationToken));
        var groups = permissions
            .GroupBy(permission => permission.Module)
            .OrderBy(group => group.Key)
            .Select(group => new PermissionGroupDto(group.Key, group.OrderBy(permission => permission.Name).Select(ToPermissionDto).ToList()))
            .ToList();

        return Result.Success<IReadOnlyList<PermissionGroupDto>>(groups);
    }

    public async Task<Result<IReadOnlyList<TenantDto>>> ListTenantsAsync(CancellationToken cancellationToken)
    {
        var tenants = await repository.ListTenantsAsync(cancellationToken);
        // Another organization's name and slug are not the caller's to read; only a holder of
        // tenants.manage_all sees them all.
        if (currentUser.UserId is not null && !currentUser.HasPermission(IdentityPermissions.TenantsManageAll))
        {
            tenants = tenants.Where(tenant => tenant.Id == currentUser.TenantId).ToList();
        }

        return Result.Success<IReadOnlyList<TenantDto>>(tenants.Select(ToTenantDto).ToList());
    }

    public async Task<Result<TenantDto>> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var validation = await createTenantValidator.ValidateAsResultAsync(request, cancellationToken);
        if (validation.IsFailure)
        {
            return Result.Failure<TenantDto>(validation.Error);
        }

        if (await repository.TenantSlugExistsAsync(request.Slug, cancellationToken))
        {
            return Result.Failure<TenantDto>(Error.Conflict("سازمانی با این شناسه قبلاً ثبت شده است."));
        }

        var tenant = new Tenant(Guid.NewGuid(), request.Name, request.Slug);
        tenant.Update(request.Name, request.Slug, request.Description, true);
        await repository.AddTenantAsync(tenant, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("tenants.create", nameof(Tenant), tenant.Id.ToString(), tenant.Slug, cancellationToken);
        return Result.Success(ToTenantDto(tenant));
    }

    /// <summary>
    /// The conflict to report when a username, mobile number or email already belongs to another
    /// user of the tenant; null when all are free. Used by administrators managing accounts.
    /// </summary>
    private async Task<Error?> FindTakenSignInDetailAsync(
        Guid tenantId, Guid? exceptUserId, string? username, string? phoneNumber, string? email, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(username)
            && await repository.UsernameExistsAsync(tenantId, username, exceptUserId, cancellationToken))
        {
            return Error.Conflict("کاربری با این نام کاربری (کد ملی) قبلاً ثبت شده است.");
        }

        if (!string.IsNullOrWhiteSpace(phoneNumber)
            && await repository.PhoneNumberExistsAsync(tenantId, phoneNumber, exceptUserId, cancellationToken))
        {
            return Error.Conflict("کاربری با این شماره تلفن همراه قبلاً ثبت شده است.");
        }

        if (!string.IsNullOrWhiteSpace(email)
            && await repository.UserEmailExistsAsync(tenantId, email, exceptUserId, cancellationToken))
        {
            return Error.Conflict("کاربری با این ایمیل قبلاً ثبت شده است.");
        }

        return null;
    }

    /// <summary>What was typed as the sign-in name, shortened. Never the password.</summary>
    /// <summary>
    /// What the sign-in log keeps of the typed name: a username or mobile number as typed (that is
    /// what an administrator needs to see), anything else - which is sometimes a password typed
    /// into the wrong field - only as its length.
    /// </summary>
    private static string AuditIdentifier(string identifier)
    {
        var value = identifier.Trim();
        if (Username.IsValid(value) || Username.IsLegacy(value) || PhoneNumber.Normalize(value) is not null)
        {
            return value.Length <= 64 ? value : value[..64];
        }

        return $"(unrecognised input, {value.Length} characters)";
    }

    /// <summary>Background work has no caller and is not limited; a person needs the permission.</summary>
    private bool CallerHolds(string permission) => currentUser.UserId is null || currentUser.HasPermission(permission);

    private static UserDto ToUserDto(User user) => IdentityMappings.ToUserDto(user);

    /// <summary>Lists the role's permissions this product manages; the others stay on the role, unlisted.</summary>
    private RoleDto ToRoleDto(Role role) =>
        new(role.Id, role.TenantId, role.Name, role.Description, role.IsSystem, role.Permissions
            .Where(grant => grant.Permission is null || _managed.IsManaged(grant.Permission))
            .Select(permission => permission.Permission?.Name ?? permission.PermissionId.ToString()).ToList());

    private static PermissionDto ToPermissionDto(Permission permission) =>
        new(permission.Id, permission.Name, permission.Module, permission.Description);

    private static TenantDto ToTenantDto(Tenant tenant) =>
        new(tenant.Id, tenant.Name, tenant.Slug, tenant.Description, tenant.IsActive);
}
