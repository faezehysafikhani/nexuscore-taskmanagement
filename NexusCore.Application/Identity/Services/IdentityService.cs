using FluentValidation;
using NexusCore.Application.Common;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
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
    IUserGroupPermissionProvider groupPermissions) : IIdentityService
{
    // One message for every way sign-in can fail on the credentials, so the answer never tells
    // whether a username or mobile number is registered.
    private const string InvalidCredentials = "Invalid username/mobile number or password.";

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
        var passwordOk = user is not null && passwordHasher.Verify(request.Password, user.PasswordHash);
        if (user is null || !user.IsActive || !passwordOk)
        {
            var captchaNext = await loginProtection.RecordFailedLoginAsync(request.Identifier, cancellationToken);
            // The reason is for administrators only; the caller always gets the same answer.
            var reason = user is null ? "unknown account" : !passwordOk ? "wrong password" : "account disabled";
            await platformService.AuditForAsync(user?.TenantId, user?.Id, "identity.login_failed", nameof(User), user?.Id.ToString(), $"{AuditIdentifier(request.Identifier)} ({reason})", cancellationToken);
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
            return Result.Failure<AuthResponse>(Error.Validation("Refresh token is required."));
        }

        var tokenHash = passwordHasher.HashToken(request.RefreshToken);
        var refreshToken = await repository.FindActiveRefreshTokenAsync(tokenHash, cancellationToken);
        if (refreshToken?.User is null || !refreshToken.User.IsActive)
        {
            return Result.Failure<AuthResponse>(Error.Unauthorized("Invalid refresh token."));
        }

        var nextRefreshToken = jwtTokenService.CreateRefreshToken();
        refreshToken.Revoke(null, passwordHasher.HashToken(nextRefreshToken));
        var nextRefreshTokenEntity = refreshToken.User.AddRefreshToken(passwordHasher.HashToken(nextRefreshToken), DateTimeOffset.UtcNow.AddDays(14), null);
        await repository.AddRefreshTokenAsync(nextRefreshTokenEntity, cancellationToken);

        var permissions = await repository.GetUserPermissionNamesAsync(refreshToken.UserId, cancellationToken);
        var accessToken = jwtTokenService.CreateAccessToken(refreshToken.User, permissions);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthResponse(accessToken.Token, nextRefreshToken, accessToken.ExpiresAtUtc, ToUserDto(refreshToken.User)));
    }

    public async Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<CurrentUserResponse>(Error.NotFound("User was not found."));
        }

        var permissions = await repository.GetUserPermissionNamesAsync(user.Id, cancellationToken);
        return Result.Success(new CurrentUserResponse(ToUserDto(user), permissions));
    }

    public async Task<Result<PagedResult<UserDto>>> ListUsersAsync(Guid? tenantId, int? pageNumber, int? pageSize, string? search, CancellationToken cancellationToken)
    {
        var users = await repository.ListUsersAsync(
    tenantId,
    pageNumber,
    pageSize,
    search,
    cancellationToken);

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

        if (await repository.GetTenantByIdAsync(request.TenantId, cancellationToken) is null)
        {
            return Result.Failure<UserDto>(Error.NotFound("Tenant was not found."));
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
        if (user is null)
        {
            return Result.Failure<UserDto>(Error.NotFound("User was not found."));
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
                return Result.Failure<UserDto>(Error.Forbidden("The built-in system administrator cannot be changed here. It changes its own password from its profile."));
            }
        }

        if (usernameChanges && !Username.IsValid(request.Username!.Trim()))
        {
            return Result.Failure<UserDto>(Error.Validation("Username must be a national code: exactly 10 digits."));
        }

        if (currentUser.UserId == user.Id && !request.IsActive)
        {
            return Result.Failure<UserDto>(Error.Validation("You cannot disable your own account."));
        }

        var taken = await FindTakenSignInDetailAsync(user.TenantId, user.Id, usernameChanges ? request.Username : null, request.PhoneNumber, request.Email, cancellationToken);
        if (taken is not null)
        {
            return Result.Failure<UserDto>(taken);
        }

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
        return Result.Success(ToUserDto(user));
    }

    public async Task<Result<UserDto>> SetUserStatusAsync(Guid userId, SetUserStatusRequest request, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(Error.NotFound("User was not found."));
        }

        if (user.IsSystem && !request.IsActive)
        {
            return Result.Failure<UserDto>(Error.Forbidden("The built-in system administrator cannot be disabled."));
        }

        if (currentUser.UserId == user.Id && !request.IsActive)
        {
            return Result.Failure<UserDto>(Error.Validation("You cannot disable your own account."));
        }

        user.SetActive(request.IsActive);
        if (!request.IsActive)
        {
            // A disabled account keeps no session: its refresh tokens stop working at once.
            await repository.RevokeRefreshTokensAsync(user.Id, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync(request.IsActive ? "users.enable" : "users.disable", nameof(User), user.Id.ToString(), user.Username, cancellationToken);
        return Result.Success(ToUserDto(user));
    }

    public async Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("User was not found."));
        }

        if (user.IsSystem)
        {
            return Result.Failure(Error.Forbidden("The built-in system administrator cannot be deleted."));
        }

        if (currentUser.UserId == user.Id)
        {
            return Result.Failure(Error.Validation("You cannot delete your own account."));
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
            return Result.Failure(Error.Conflict("The user is still referenced by other records (for example tasks they own). Deactivate the account instead, or reassign that data first."));
        }
        await platformService.AuditAsync("users.delete", nameof(User), user.Id.ToString(), auditDetails, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AssignRolesAsync(Guid userId, AssignUserRolesRequest request, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("User was not found."));
        }

        var guard = GuardAccessChange(user);
        if (guard.IsFailure)
        {
            return guard;
        }

        user.SetRoles(request.RoleIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("users.assign_roles", nameof(User), user.Id.ToString(), string.Join(",", request.RoleIds), cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// What the user may do and why: every permission with whether it comes from the user
    /// directly, from a role or from an organisational group. Only direct grants are editable
    /// on the user; the others are changed on the role or group.
    /// </summary>
    public async Task<Result<UserPermissionsDto>> GetUserAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserPermissionsDto>(Error.NotFound("User was not found."));
        }

        var permissions = await repository.ListPermissionsAsync(cancellationToken);
        var roles = await repository.ListRolesAsync(user.TenantId, cancellationToken);
        var userRoleIds = user.Roles.Select(role => role.RoleId).ToHashSet();
        var grantingRoles = roles
            .Where(role => userRoleIds.Contains(role.Id))
            .SelectMany(role => role.Permissions.Select(grant => (grant.PermissionId, role.Name)))
            .GroupBy(pair => pair.PermissionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(pair => pair.Name).Distinct().ToList());
        var grantingGroups = await groupPermissions.GetGrantingGroupsAsync(user.Id, cancellationToken);
        var direct = user.Permissions.Select(grant => grant.PermissionId).ToHashSet();

        var entries = permissions
            .OrderBy(permission => permission.Module).ThenBy(permission => permission.Name)
            .Select(permission =>
            {
                var byRole = grantingRoles.GetValueOrDefault(permission.Id, []);
                var byGroup = grantingGroups.GetValueOrDefault(permission.Id, []);
                return new UserPermissionEntryDto(
                    permission.Id, permission.Name, permission.Module, permission.Description,
                    direct.Contains(permission.Id), byRole.Count > 0, byRole, byGroup.Count > 0, byGroup);
            })
            .ToList();

        return Result.Success(new UserPermissionsDto(
            user.Id, user.DisplayName, user.Email,
            user.Roles.Select(role => role.Role?.Name ?? role.RoleId.ToString()).ToList(),
            entries));
    }

    /// <summary>
    /// Replaces the permissions granted directly to the user. A caller can only hand out
    /// permissions they hold themselves, so this cannot be used to escalate.
    /// </summary>
    public async Task<Result> SetUserDirectPermissionsAsync(Guid userId, AssignUserPermissionsRequest request, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("User was not found."));
        }

        var guard = GuardAccessChange(user);
        if (guard.IsFailure)
        {
            return guard;
        }

        var requested = request.PermissionIds.Distinct().ToList();
        var known = await repository.GetPermissionsByIdsAsync(requested, cancellationToken);
        if (known.Count != requested.Count)
        {
            return Result.Failure(Error.Validation("One or more permissions do not exist."));
        }

        var alreadyDirect = user.Permissions.Select(grant => grant.PermissionId).ToHashSet();
        var added = known.Where(permission => !alreadyDirect.Contains(permission.Id)).ToList();
        if (added.Count > 0 && currentUser.UserId is { } callerId)
        {
            var callerPermissions = (await repository.GetUserPermissionNamesAsync(callerId, cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var notHeld = added.Where(permission => !callerPermissions.Contains(permission.Name)).Select(permission => permission.Name).ToList();
            if (notHeld.Count > 0)
            {
                return Result.Failure(Error.Forbidden("You can only grant permissions you have yourself: " + string.Join(", ", notHeld)));
            }
        }

        user.SetDirectPermissions(requested);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("users.assign_permissions", nameof(User), user.Id.ToString(), string.Join(",", known.Select(permission => permission.Name)), cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Nobody changes the built-in administrator's access, and nobody changes their own - so
    /// no one can make themselves more powerful through the user screens.
    /// </summary>
    private Result GuardAccessChange(User target)
    {
        if (target.IsSystem)
        {
            return Result.Failure(Error.Forbidden("The access of the built-in system administrator cannot be changed."));
        }

        if (currentUser.UserId == target.Id)
        {
            return Result.Failure(Error.Forbidden("You cannot change your own roles or permissions."));
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<RoleDto>>> ListRolesAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var roles = await repository.ListRolesAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<RoleDto>>(roles.Select(ToRoleDto).ToList());
    }

    public async Task<Result<RoleDto>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var validation = await createRoleValidator.ValidateAsResultAsync(request, cancellationToken);
        if (validation.IsFailure)
        {
            return Result.Failure<RoleDto>(validation.Error);
        }

        if (await repository.RoleNameExistsAsync(request.TenantId, request.Name, cancellationToken))
        {
            return Result.Failure<RoleDto>(Error.Conflict("A role with this name already exists in the tenant."));
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
        if (role is null)
        {
            return Result.Failure<RoleDto>(Error.NotFound("Role was not found."));
        }

        role.Update(request.Name, request.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("roles.update", nameof(Role), role.Id.ToString(), role.Name, cancellationToken);
        return Result.Success(ToRoleDto(role));
    }

    public async Task<Result> AssignPermissionsAsync(Guid roleId, AssignRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        var role = await repository.GetRoleByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure(Error.NotFound("Role was not found."));
        }

        role.SetPermissions(request.PermissionIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("roles.assign_permissions", nameof(Role), role.Id.ToString(), string.Join(",", request.PermissionIds), cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PermissionGroupDto>>> ListPermissionsGroupedAsync(CancellationToken cancellationToken)
    {
        var permissions = await repository.ListPermissionsAsync(cancellationToken);
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
            return Result.Failure<TenantDto>(Error.Conflict("A tenant with this slug already exists."));
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
            return Error.Conflict("A user with this username already exists in the tenant.");
        }

        if (!string.IsNullOrWhiteSpace(phoneNumber)
            && await repository.PhoneNumberExistsAsync(tenantId, phoneNumber, exceptUserId, cancellationToken))
        {
            return Error.Conflict("A user with this mobile number already exists in the tenant.");
        }

        if (!string.IsNullOrWhiteSpace(email)
            && await repository.UserEmailExistsAsync(tenantId, email, exceptUserId, cancellationToken))
        {
            return Error.Conflict("A user with this email already exists in the tenant.");
        }

        return null;
    }

    /// <summary>What was typed as the sign-in name, shortened. Never the password.</summary>
    private static string AuditIdentifier(string identifier)
    {
        var value = identifier.Trim();
        return value.Length <= 64 ? value : value[..64];
    }

    private static UserDto ToUserDto(User user) => IdentityMappings.ToUserDto(user);

    private static RoleDto ToRoleDto(Role role) =>
        new(role.Id, role.TenantId, role.Name, role.Description, role.IsSystem, role.Permissions.Select(permission => permission.Permission?.Name ?? permission.PermissionId.ToString()).ToList());

    private static PermissionDto ToPermissionDto(Permission permission) =>
        new(permission.Id, permission.Name, permission.Module, permission.Description);

    private static TenantDto ToTenantDto(Tenant tenant) =>
        new(tenant.Id, tenant.Name, tenant.Slug, tenant.Description, tenant.IsActive);
}
