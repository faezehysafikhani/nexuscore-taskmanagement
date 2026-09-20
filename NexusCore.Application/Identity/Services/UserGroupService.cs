using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
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
    IPlatformService platformService) : IUserGroupService
{
    public async Task<Result<IReadOnlyList<UserGroupDto>>> ListAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var groups = await repository.ListAsync(tenantId, cancellationToken);
        var users = await LoadMemberUsersAsync(groups, cancellationToken);
        return Result.Success<IReadOnlyList<UserGroupDto>>(groups.Select(group => ToDto(group, users)).ToList());
    }

    public async Task<Result<UserGroupDto>> GetAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await repository.GetByIdAsync(groupId, cancellationToken);
        if (group is null)
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
        if (group is null)
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
        if (group is null)
        {
            return Result.Failure(Error.NotFound("User group was not found."));
        }

        var known = (await identityRepository.ListPermissionsAsync(cancellationToken)).Select(permission => permission.Id).ToHashSet();
        if (request.PermissionIds.Any(id => !known.Contains(id)))
        {
            return Result.Failure(Error.Validation("One or more permissions do not exist."));
        }

        group.SetPermissions(request.PermissionIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("groups.assign_permissions", nameof(UserGroup), group.Id.ToString(), string.Join(",", request.PermissionIds), cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AssignMembersAsync(Guid groupId, AssignGroupMembersRequest request, CancellationToken cancellationToken)
    {
        var group = await repository.GetByIdAsync(groupId, cancellationToken);
        if (group is null)
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

    private async Task<IReadOnlyList<User>> LoadMemberUsersAsync(IReadOnlyList<UserGroup> groups, CancellationToken cancellationToken)
    {
        var userIds = groups.SelectMany(group => group.Members.Select(member => member.UserId)).Distinct().ToList();
        return userIds.Count == 0 ? [] : await repository.ListUsersAsync(userIds, cancellationToken);
    }

    private static UserGroupDto ToDto(UserGroup group, IReadOnlyList<User> users)
    {
        var byId = users.ToDictionary(user => user.Id);

        return new UserGroupDto(
            group.Id,
            group.TenantId,
            group.Name,
            group.Description,
            group.IsActive,
            group.Members.Count,
            group.Permissions.Select(permission => permission.Permission?.Name ?? string.Empty).Where(name => name.Length > 0).OrderBy(name => name).ToList(),
            group.Permissions.Select(permission => permission.PermissionId).ToList(),
            group.Members
                .Where(member => byId.ContainsKey(member.UserId))
                .Select(member => new UserGroupMemberDto(member.UserId, byId[member.UserId].DisplayName, byId[member.UserId].Email))
                .OrderBy(member => member.DisplayName)
                .ToList());
    }
}
