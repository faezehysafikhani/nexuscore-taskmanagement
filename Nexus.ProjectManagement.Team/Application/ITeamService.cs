using Nexus.ProjectManagement.Team.Application.Dtos;
using NexusCore.Application.Identity.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Team.Application;

public interface ITeamService
{
    Task<Result<IReadOnlyList<ProjectMemberDto>>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<ProjectMemberDto>> AddMemberAsync(AddProjectMemberRequest request, CancellationToken cancellationToken);
    Task<Result> RemoveMemberAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>Tenant users not already on the project's team - backed by NexusCore's own
    /// IIdentityService, since Users is a required (always-present) dependency.</summary>
    Task<Result<IReadOnlyList<UserDto>>> ListAvailableUsersAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<GovernanceRoleDto>>> ListGovernanceRolesAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<GovernanceRoleDto>> CreateGovernanceRoleAsync(CreateGovernanceRoleRequest request, CancellationToken cancellationToken);
    Task<Result<GovernanceRoleDto>> UpdateGovernanceRoleAsync(Guid id, UpdateGovernanceRoleRequest request, CancellationToken cancellationToken);
}
