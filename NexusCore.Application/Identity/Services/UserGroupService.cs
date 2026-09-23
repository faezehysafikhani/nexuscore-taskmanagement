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
    IOptions<ManagedPermissionOptions> managedPermissionOptions) : IUserGroupService
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
            return Result.Failure<UserGroupDto>(Error.NotFound("User group was not found."));
        }

        var users = await LoadMemberUsersAsync([group], cancellationToken);
        return Result.Success(ToDto(group, users));
    }

    public async Task<Result<UserGroupDto>> CreateAsync(CreateUserGroupRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<UserGroupDto>(Error.Validation("Group name is required."));
        }

        if (!currentUser.CanAccessTenant(request.TenantId))
        {
            return Result.Failure<UserGroupDto>(Error.NotFound("Tenant was not found."));
        }

        var normalized = request.Name.Trim().ToUpperInvariant();
        if (await repository.NameExistsAsync(request.TenantId, normalized, null, cancellationToken))
        {
            return Result.Failure<UserGroupDto>(Error.Conflict("A group with this name already exists."));
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
            return Result.Failure<UserGroupDto>(Error.NotFound("User group was not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<UserGroupDto>(Error.Validation("Group name is required."));
        }

        var normalized = request.Name.Trim().ToUpperInvariant();
        if (await repository.NameExistsAsync(group.TenantId, normalized, group.Id, cancellationToken))
        {
            return Result.Failure<UserGroupDto>(Error.Conflict("A group with this name already exists."));
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
            return Result.Failure(Error.NotFound("User group was not found."));
        }

        // A personal work team is managed by its owner; letting it carry permissions would let any
        // user grant themselves access by joining their own team.
        if (group.IsPersonalTeam)
        {
            return Result.Failure(Error.Validation("A personal work team cannot carry permissions."));
        }

        var known = (await identityRepository.ListPermissionsAsync(cancellationToken)).ToDictionary(permission => permission.Id);
        if (request.PermissionIds.Any(id => !known.ContainsKey(id)))
        {
            return Result.Failure(Error.Validation("One or more permissions do not exist."));
        }

        var unmanaged = request.PermissionIds.Distinct().Select(id => known[id]).Where(permission => !_managed.IsManaged(permission)).Select(permission => permission.Name).ToList();
        if (unmanaged.Count > 0)
        {
            return Result.Failure(Error.Validation("These permissions are not managed in this product: " + string.Join(", ", unmanaged)));
        }

        // Same rule as for users and roles: nobody hands out a permission they do not have.
        var already = group.Permissions.Select(grant => grant.PermissionId).ToHashSet();
        var notHeld = request.PermissionIds.Distinct()
            .Where(id => !already.Contains(id))
            .Select(id => known[id].Name)
            .Where(name => currentUser.UserId is not null && !currentUser.HasPermission(name))
            .ToList();
        if (notHeld.Count > 0)
        {
            return Result.Failure(Error.Forbidden("You can only grant permissions you have yourself: " + string.Join(", ", notHeld)));
        }

        // The group's permissions of modules this product does not manage stay as they were.
        group.SetPermissions(request.PermissionIds.Concat(group.Permissions
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
            return Result.Failure(Error.NotFound("User group was not found."));
        }

        var users = await repository.ListUsersAsync(request.UserIds, cancellationToken);
        if (users.Count != request.UserIds.Distinct().Count())
        {
            return Result.Failure(Error.Validation("One or more users do not exist."));
        }

        // A group belongs to one tenant; its members must too.
        if (users.Any(user => user.TenantId != group.TenantId))
        {
            return Result.Failure(Error.Validation("All members must belong to the same tenant as the group."));
        }

        group.SetMembers(request.UserIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("groups.manage_members", nameof(UserGroup), group.Id.ToString(), string.Join(",", request.UserIds), cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await repository.GetByIdAsync(groupId, cancellationToken);
        if (group is null || !currentUser.CanAccessTenant(group.TenantId))
        {
            return Result.Failure(Error.NotFound("User group was not found."));
        }

        return await RemoveAsync(group, "groups.delete", cancellationToken);
    }

    public async Task<Result<IReadOnlyList<UserGroupDto>>> ListMyTeamsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } ownerId || currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<UserGroupDto>>(Error.Unauthorized());
        }

        var teams = await repository.ListOwnedAsync(tenantId, ownerId, cancellationToken);
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
            return Result.Failure<UserGroupDto>(Error.Conflict("You already have a team with this name."));
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
            return Result.Failure<UserGroupDto>(Error.Conflict("You already have a team with this name."));
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
            return Result.Failure<UserGroupDto>(Error.Validation("One or more users do not exist."));
        }

        if (users.Any(user => user.TenantId != team.TenantId))
        {
            return Result.Failure<UserGroupDto>(Error.Validation("All members must belong to the same tenant as the team."));
        }

        team.SetMembers(memberIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("groups.manage_own_members", nameof(UserGroup), team.Id.ToString(), string.Join(",", memberIds), cancellationToken);
        return Result.Success(ToDto(team, users));
    }

    public async Task<Result> DeleteMyTeamAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var owned = await GetOwnedTeamAsync(groupId, cancellationToken);
        return owned.IsFailure
            ? Result.Failure(owned.Error)
            : await RemoveAsync(owned.Value!, "groups.delete_own", cancellationToken);
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
            ? Result.Failure<UserGroup>(Error.NotFound("Team was not found."))
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
            return Result.Failure(Error.Conflict("The team is still in use (for example, tasks are assigned to it). Reassign them first."));
        }

        await platformService.AuditAsync(auditAction, nameof(UserGroup), group.Id.ToString(), group.Name, cancellationToken);
        return Result.Success();
    }

    private static Result ValidateTeamName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? Result.Failure(Error.Validation("Team name is required."))
            : name.Trim().Length > 128
                ? Result.Failure(Error.Validation("Team name must be at most 128 characters."))
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
