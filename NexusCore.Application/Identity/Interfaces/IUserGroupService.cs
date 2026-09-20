using NexusCore.Application.Identity.Dtos;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Interfaces;

/// <summary>Optional user-group feature. Only registered when the feature is enabled.</summary>
public interface IUserGroupService
{
    Task<Result<IReadOnlyList<UserGroupDto>>> ListAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<Result<UserGroupDto>> GetAsync(Guid groupId, CancellationToken cancellationToken);
    Task<Result<UserGroupDto>> CreateAsync(CreateUserGroupRequest request, CancellationToken cancellationToken);
    Task<Result<UserGroupDto>> UpdateAsync(Guid groupId, UpdateUserGroupRequest request, CancellationToken cancellationToken);
    Task<Result> AssignPermissionsAsync(Guid groupId, AssignGroupPermissionsRequest request, CancellationToken cancellationToken);
    Task<Result> AssignMembersAsync(Guid groupId, AssignGroupMembersRequest request, CancellationToken cancellationToken);
}
