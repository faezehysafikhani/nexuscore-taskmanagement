using Nexus.ProjectManagement.Team.Application.Dtos;
using NexusCore.Application.Identity.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Team.Application;

public interface ITeamService
{
    Task<Result<IReadOnlyList<ProjectMemberDto>>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken);
    /// <summary>The members of several projects in one call (the project list screens need everybody's team at once).</summary>
    Task<Result<IReadOnlyList<ProjectMemberDto>>> ListMembersForProjectsAsync(ListMembersForProjectsRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>Makes the project's team exactly the given people (adds the missing, removes the rest).</summary>
    Task<Result<IReadOnlyList<ProjectMemberDto>>> SetTeamAsync(SetProjectTeamRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<ProjectMemberDto>> AddMemberAsync(AddProjectMemberRequest request, CancellationToken cancellationToken);
    Task<Result<ProjectMemberDto>> UpdateMemberAsync(Guid memberId, UpdateProjectMemberRequest request, CancellationToken cancellationToken);
    Task<Result> RemoveMemberAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>Tenant users not already on the project's team - backed by NexusCore's own
    /// IIdentityService, since Users is a required (always-present) dependency.</summary>
    Task<Result<IReadOnlyList<UserDto>>> ListAvailableUsersAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<GovernanceRoleDto>>> ListGovernanceRolesAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<GovernanceRoleDto>> CreateGovernanceRoleAsync(CreateGovernanceRoleRequest request, CancellationToken cancellationToken);
    Task<Result<GovernanceRoleDto>> UpdateGovernanceRoleAsync(Guid id, UpdateGovernanceRoleRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteGovernanceRoleAsync(Guid id, CancellationToken cancellationToken);
}
