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
    Task<Result> DeleteAsync(Guid groupId, CancellationToken cancellationToken);

    // Personal work teams of the signed-in user.
    Task<Result<IReadOnlyList<UserGroupDto>>> ListMyTeamsAsync(CancellationToken cancellationToken);
    Task<Result<UserGroupDto>> CreateMyTeamAsync(CreateMyTeamRequest request, CancellationToken cancellationToken);
    Task<Result<UserGroupDto>> UpdateMyTeamAsync(Guid groupId, UpdateMyTeamRequest request, CancellationToken cancellationToken);
    Task<Result<UserGroupDto>> SetMyTeamMembersAsync(Guid groupId, SetMyTeamMembersRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteMyTeamAsync(Guid groupId, CancellationToken cancellationToken);
}
