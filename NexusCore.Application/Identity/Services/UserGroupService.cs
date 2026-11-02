using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Services;

/// <summary>Optional user-group feature. Registered only when the feature is enabled.</summary>
public sealed class UserGroupService(
    IUserGroupRepository repository,
    IIdentityRepository identityRepository,
    IUnitOfWork unitOfWork,
    IPlatformService platformService,
    ICurrentUserContext currentUser,
    IOptions<ManagedPermissionOptions> managedPermissionOptions,
    IUserGroupMembershipNotifier membershipNotifier,
    IWorkTeamMemberScope memberScope) : IUserGroupService
{
    private readonly ManagedPermissionOptions _managed = managedPermissionOptions.Value;

    public async Task<Result<IReadOnlyList<UserGroupDto>>> ListAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var groups = await repository.ListAsync(currentUser.ResolveTenant(tenantId), cancellationToken);
        var users = await LoadMemberUsersAsync(groups, cancellationToken);
        return Result.Success<IReadOnlyList<UserGroupDto>>(groups.Select(group => ToDto(group, users)).ToList());
    }

    public async Task<Result<UserGroupDto>> GetAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await repository.GetByIdAsync(groupId, cancellationToken);
        if (group is null || !currentUser.CanAccessTenant(group.TenantId))
        {
            return Result.Failure<UserGroupDto>(Error.NotFound("گروه کاربری یافت نشد."));
        }

        var users = await LoadMemberUsersAsync([group], cancellationToken);
        return Result.Success(ToDto(group, users));
    }

    public async Task<Result<UserGroupDto>> CreateAsync(CreateUserGroupRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<UserGroupDto>(Error.Validation("نام گروه را وارد کنید."));
        }

        if (!currentUser.CanAccessTenant(request.TenantId))
        {
            return Result.Failure<UserGroupDto>(Error.NotFound("سازمان یافت نشد."));
        }

        var normalized = request.Name.Trim().ToUpperInvariant();
        if (await repository.NameExistsAsync(request.TenantId, normalized, null, cancellationToken))
        {
            return Result.Failure<UserGroupDto>(Error.Conflict("گروهی با این نام قبلاً ثبت شده است."));
        }

        var group = new UserGroup(Guid.NewGuid(), request.TenantId, request.Name, request.Description);
        await repository.AddAsync(group, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("groups.create", nameof(UserGroup), group.Id.ToString(), group.Name, cancellationToken);

        return Result.Success(ToDto(group, []));
    }

    public async Task<Result<UserGroupDto>> UpdateAsync(Guid groupId, UpdateUserGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await repository.GetByIdAsync(groupId, cancellationToken);
        if (group is null || !currentUser.CanAccessTenant(group.TenantId))
        {
            return Result.Failure<UserGroupDto>(Error.NotFound("گروه کاربری یافت نشد."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<UserGroupDto>(Error.Validation("نام گروه را وارد کنید."));
        }

        var normalized = request.Name.Trim().ToUpperInvariant();
        if (await repository.NameExistsAsync(group.TenantId, normalized, group.Id, cancellationToken))
        {
            return Result.Failure<UserGroupDto>(Error.Conflict("گروهی با این نام قبلاً ثبت شده است."));
        }

        group.Update(request.Name, request.Description, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("groups.update", nameof(UserGroup), group.Id.ToString(), group.Name, cancellationToken);

        var users = await LoadMemberUsersAsync([group], cancellationToken);
        return Result.Success(ToDto(group, users));
    }

    public async Task<Result> AssignPermissionsAsync(Guid groupId, AssignGroupPermissionsRequest request, CancellationToken cancellationToken)
    {
        var group = await repository.GetByIdAsync(groupId, cancellationToken);
        if (group is null || !currentUser.CanAccessTenant(group.TenantId))
        {
            return Result.Failure(Error.NotFound("گروه کاربری یافت نشد."));
        }

        // A personal work team is managed by its owner; letting it carry permissions would let any
        // user grant themselves access by joining their own team.
        if (group.IsPersonalTeam)
        {
            return Result.Failure(Error.Validation("به تیم کاری شخصی نمی‌توان مجوز داد."));
        }

        if (request.PermissionIds is null)
        {
            return Result.Failure(Error.Validation("فهرست مجوزها را بفرستید."));
        }

        var known = (await identityRepository.ListPermissionsAsync(cancellationToken)).ToDictionary(permission => permission.Id);
        if (request.PermissionIds.Any(id => !known.ContainsKey(id)))
        {
            return Result.Failure(Error.Validation("یک یا چند مجوز انتخاب‌شده وجود ندارد."));
        }

        var unmanaged = request.PermissionIds.Distinct().Select(id => known[id]).Where(permission => !_managed.IsManaged(permission)).Select(permission => permission.Name).ToList();
        if (unmanaged.Count > 0)
        {
            return Result.Failure(Error.Validation("These permissions are not managed in this product: " + string.Join(", ", unmanaged)));
        }

        // Prerequisites are saved with the permission that needs them (same rule as for users and roles).
        var requestedIds = request.PermissionIds.Distinct().ToList();
        var byName = known.Values.ToDictionary(permission => permission.Name, StringComparer.Ordinal);
        foreach (var impliedName in PermissionPrerequisites.ImpliedBy(requestedIds.Select(id => known[id].Name)))
        {
            if (byName.TryGetValue(impliedName, out var implied) && _managed.IsManaged(implied) && !requestedIds.Contains(implied.Id))
            {
                requestedIds.Add(implied.Id);
            }
        }

        // Same rule as for users and roles: nobody hands out a permission they do not have.
        var already = group.Permissions.Select(grant => grant.PermissionId).ToHashSet();
        var notHeld = requestedIds
            .Where(id => !already.Contains(id))
            .Select(id => known[id].Name)
            .Where(name => currentUser.UserId is not null && !currentUser.HasPermission(name))
            .ToList();
        if (notHeld.Count > 0)
        {
            return Result.Failure(Error.Forbidden("You can only grant permissions you have yourself: " + string.Join(", ", notHeld)));
        }

        // The group's permissions of modules this product does not manage stay as they were.
        group.SetPermissions(requestedIds.Concat(group.Permissions
            .Where(grant => known.TryGetValue(grant.PermissionId, out var permission) && !_managed.IsManaged(permission))
            .Select(grant => grant.PermissionId)).Distinct().ToList());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("groups.assign_permissions", nameof(UserGroup), group.Id.ToString(), string.Join(",", request.PermissionIds), cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AssignMembersAsync(Guid groupId, AssignGroupMembersRequest request, CancellationToken cancellationToken)
    {
        var group = await repository.GetByIdAsync(groupId, cancellationToken);
        if (group is null || !currentUser.CanAccessTenant(group.TenantId))
        {
            return Result.Failure(Error.NotFound("گروه کاربری یافت نشد."));
        }

        var users = await repository.ListUsersAsync(request.UserIds, cancellationToken);
        if (users.Count != request.UserIds.Distinct().Count())
        {
            return Result.Failure(Error.Validation("یک یا چند کاربر انتخاب‌شده وجود ندارد."));
        }

        // A group belongs to one tenant; its members must too.
        if (users.Any(user => user.TenantId != group.TenantId))
        {
            return Result.Failure(Error.Validation("همه اعضا باید از سازمان همین گروه باشند."));
        }

        var previousMemberIds = group.Members.Select(member => member.UserId).ToList();

        // A group hands its permissions to its members, so adding someone is granting those
        // permissions: nobody adds a person - themselves included - to a group whose permissions
        // they do not hold themselves (same rule as for roles and direct permissions).
        var newMemberIds = request.UserIds.Distinct().Except(previousMemberIds).ToList();
        if (newMemberIds.Count > 0 && group.Permissions.Count > 0 && currentUser.UserId is not null)
        {
            var permissionsById = (await identityRepository.ListPermissionsAsync(cancellationToken)).ToDictionary(permission => permission.Id);
            var notHeld = group.Permissions
                .Select(grant => permissionsById.GetValueOrDefault(grant.PermissionId))
                .Where(permission => permission is not null && _managed.IsManaged(permission) && !currentUser.HasPermission(permission.Name))
                .Select(permission => permission!.Name)
                .ToList();
            if (notHeld.Count > 0)
            {
                return Result.Failure(Error.Forbidden("You can only add members to a group whose permissions you have yourself: " + string.Join(", ", notHeld)));
            }
        }

        group.SetMembers(request.UserIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("groups.manage_members", nameof(UserGroup), group.Id.ToString(), string.Join(",", request.UserIds), cancellationToken);
        await NotifyNewMembersAsync(group, previousMemberIds, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await repository.GetByIdAsync(groupId, cancellationToken);
        if (group is null || !currentUser.CanAccessTenant(group.TenantId))
        {
            return Result.Failure(Error.NotFound("گروه کاربری یافت نشد."));
        }

        return await RemoveAsync(group, "groups.delete", cancellationToken);
    }

    public async Task<Result<IReadOnlyList<UserGroupDto>>> ListMyTeamsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<UserGroupDto>>(Error.Unauthorized());
        }

        var teams = await repository.ListVisibleToUserAsync(tenantId, userId, cancellationToken);
        var users = await LoadMemberUsersAsync(teams, cancellationToken);
        return Result.Success<IReadOnlyList<UserGroupDto>>(teams.Select(team => ToDto(team, users)).ToList());
    }

    public async Task<Result<UserGroupDto>> CreateMyTeamAsync(CreateMyTeamRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } ownerId || currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<UserGroupDto>(Error.Unauthorized());
        }

        var nameCheck = ValidateTeamName(request.Name);
        if (nameCheck.IsFailure)
        {
            return Result.Failure<UserGroupDto>(nameCheck.Error);
        }

        if (await repository.NameExistsAsync(tenantId, request.Name.Trim().ToUpperInvariant(), null, cancellationToken, ownerId))
        {
            return Result.Failure<UserGroupDto>(Error.Conflict("شما قبلاً تیمی با این نام ساخته‌اید."));
        }

        // The owner is always the first member of their own team.
        var team = new UserGroup(Guid.NewGuid(), tenantId, request.Name, request.Description, ownerId);
        team.SetMembers([ownerId]);
        await repository.AddAsync(team, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("groups.create_own", nameof(UserGroup), team.Id.ToString(), team.Name, cancellationToken);

        var users = await LoadMemberUsersAsync([team], cancellationToken);
        return Result.Success(ToDto(team, users));
    }

    public async Task<Result<UserGroupDto>> UpdateMyTeamAsync(Guid groupId, UpdateMyTeamRequest request, CancellationToken cancellationToken)
    {
        var owned = await GetOwnedTeamAsync(groupId, cancellationToken);
        if (owned.IsFailure)
        {
            return Result.Failure<UserGroupDto>(owned.Error);
        }

        var team = owned.Value!;
        var nameCheck = ValidateTeamName(request.Name);
        if (nameCheck.IsFailure)
        {
            return Result.Failure<UserGroupDto>(nameCheck.Error);
        }

        if (await repository.NameExistsAsync(team.TenantId, request.Name.Trim().ToUpperInvariant(), team.Id, cancellationToken, team.OwnerUserId))
        {
            return Result.Failure<UserGroupDto>(Error.Conflict("شما قبلاً تیمی با این نام ساخته‌اید."));
        }

        team.Update(request.Name, request.Description, team.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("groups.update_own", nameof(UserGroup), team.Id.ToString(), team.Name, cancellationToken);

        var users = await LoadMemberUsersAsync([team], cancellationToken);
        return Result.Success(ToDto(team, users));
    }

    public async Task<Result<UserGroupDto>> SetMyTeamMembersAsync(Guid groupId, SetMyTeamMembersRequest request, CancellationToken cancellationToken)
    {
        var owned = await GetOwnedTeamAsync(groupId, cancellationToken);
        if (owned.IsFailure)
        {
            return Result.Failure<UserGroupDto>(owned.Error);
        }

        var team = owned.Value!;
        var memberIds = (request.UserIds ?? []).Append(team.OwnerUserId!.Value).Distinct().ToList();

        var users = await repository.ListUsersAsync(memberIds, cancellationToken);
        if (users.Count != memberIds.Count)
        {
            return Result.Failure<UserGroupDto>(Error.Validation("یک یا چند کاربر انتخاب‌شده وجود ندارد."));
        }

        if (users.Any(user => user.TenantId != team.TenantId))
        {
            return Result.Failure<UserGroupDto>(Error.Validation("همه اعضا باید از سازمان همین تیم باشند."));
        }

        var previousMemberIds = team.Members.Select(member => member.UserId).ToList();

        // Only the people being added are checked: members the team already has stay.
        var added = memberIds.Except(previousMemberIds).ToList();
        if (added.Count > 0
            && await memberScope.GetLimitAsync(team.TenantId, team.OwnerUserId!.Value, cancellationToken) is { } limit
            && added.Any(userId => !limit.AllowedUserIds.Contains(userId)))
        {
            return Result.Failure<UserGroupDto>(Error.Forbidden(limit.IsCompany
                ? "فقط افراد شرکت خودتان را می‌توانید به تیم اضافه کنید."
                : "فقط افراد معاونت خودتان را می‌توانید به تیم اضافه کنید؛ برای افزودن افراد سایر معاونت‌ها دسترسی لازم را ندارید."));
        }

        team.SetMembers(memberIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("groups.manage_own_members", nameof(UserGroup), team.Id.ToString(), string.Join(",", memberIds), cancellationToken);
        await NotifyNewMembersAsync(team, previousMemberIds, cancellationToken);
        return Result.Success(ToDto(team, users));
    }

    public async Task<Result<MyTeamMemberScopeDto>> GetMyTeamMemberScopeAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<MyTeamMemberScopeDto>(Error.Unauthorized());
        }

        var limit = await memberScope.GetLimitAsync(tenantId, userId, cancellationToken);
        return Result.Success(limit is null
            ? new MyTeamMemberScopeDto(false, [], null)
            : new MyTeamMemberScopeDto(true, limit.AllowedUserIds.ToList(), limit.ScopeName));
    }

    public async Task<Result> DeleteMyTeamAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var owned = await GetOwnedTeamAsync(groupId, cancellationToken);
        return owned.IsFailure
            ? Result.Failure(owned.Error)
            : await RemoveAsync(owned.Value!, "groups.delete_own", cancellationToken);
    }

    /// <summary>
    /// Lets the people who were not members before know they were added - never the person
    /// who added them (they already know).
    /// </summary>
    private async Task NotifyNewMembersAsync(UserGroup group, IReadOnlyCollection<Guid> previousMemberIds, CancellationToken cancellationToken)
    {
        var added = group.Members
            .Select(member => member.UserId)
            .Where(userId => !previousMemberIds.Contains(userId) && userId != currentUser.UserId)
            .Distinct()
            .ToList();
        if (added.Count == 0)
        {
            return;
        }

        string? addedByName = null;
        if (currentUser.UserId is { } actorId)
        {
            addedByName = (await repository.ListUsersAsync([actorId], cancellationToken)).FirstOrDefault()?.DisplayName;
        }

        await membershipNotifier.MembersAddedAsync(
            new UserGroupMembersAdded(group.TenantId, group.Id, group.Name, group.IsPersonalTeam, addedByName, added),
            cancellationToken);
    }

    /// <summary>
    /// The caller's own team, or the same "not found" for a team that does not exist and one
    /// that belongs to somebody else - so ids of other people's teams are not confirmed.
    /// </summary>
    private async Task<Result<UserGroup>> GetOwnedTeamAsync(Guid groupId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } ownerId || currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<UserGroup>(Error.Unauthorized());
        }

        var team = await repository.GetByIdAsync(groupId, cancellationToken);
        return team is null || team.TenantId != tenantId || team.OwnerUserId != ownerId
            ? Result.Failure<UserGroup>(Error.NotFound("تیم یافت نشد."))
            : Result.Success(team);
    }

    private async Task<Result> RemoveAsync(UserGroup group, string auditAction, CancellationToken cancellationToken)
    {
        repository.Remove(group);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Other modules (e.g. tasks assigned to this team) still point at the group.
            return Result.Failure(Error.Conflict("این تیم هنوز در حال استفاده است (مثلاً فعالیت‌هایی به آن واگذار شده). ابتدا آن‌ها را به تیم دیگری واگذار کنید."));
        }

        await platformService.AuditAsync(auditAction, nameof(UserGroup), group.Id.ToString(), group.Name, cancellationToken);
        return Result.Success();
    }

    private static Result ValidateTeamName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? Result.Failure(Error.Validation("نام تیم را وارد کنید."))
            : name.Trim().Length > 128
                ? Result.Failure(Error.Validation("نام تیم نباید بیشتر از ۱۲۸ کاراکتر باشد."))
                : Result.Success();

    private async Task<IReadOnlyList<User>> LoadMemberUsersAsync(IReadOnlyList<UserGroup> groups, CancellationToken cancellationToken)
    {
        var userIds = groups.SelectMany(group => group.Members.Select(member => member.UserId)).Distinct().ToList();
        return userIds.Count == 0 ? [] : await repository.ListUsersAsync(userIds, cancellationToken);
    }

    private UserGroupDto ToDto(UserGroup group, IReadOnlyList<User> users)
    {
        var byId = users.ToDictionary(user => user.Id);
        // Lists the group's permissions this product manages; the others stay on the group, unlisted.
        var permissions = group.Permissions.Where(grant => grant.Permission is null || _managed.IsManaged(grant.Permission)).ToList();

        return new UserGroupDto(
            group.Id,
            group.TenantId,
            group.Name,
            group.Description,
            group.IsActive,
            group.Members.Count,
            permissions.Select(permission => permission.Permission?.Name ?? string.Empty).Where(name => name.Length > 0).OrderBy(name => name).ToList(),
            permissions.Select(permission => permission.PermissionId).ToList(),
            group.Members
                .Where(member => byId.ContainsKey(member.UserId))
                .Select(member => new UserGroupMemberDto(member.UserId, byId[member.UserId].DisplayName, byId[member.UserId].Email))
                .OrderBy(member => member.DisplayName)
                .ToList(),
            group.OwnerUserId);
    }
}
