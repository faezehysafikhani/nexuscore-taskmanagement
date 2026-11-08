using Nexus.ProjectManagement.Team.Domain;

namespace Nexus.ProjectManagement.Team.Application;

public interface ITeamRepository
{
    Task<ProjectMember?> GetMemberByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>The members of several projects at once. A repository written before this existed reports it as unsupported.</summary>
    Task<IReadOnlyList<ProjectMember>> ListMembersForProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This team repository cannot list the members of several projects.");

    Task<bool> IsMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The projects of the tenant the user is a team member of.</summary>
    Task<IReadOnlyList<Guid>> ListProjectIdsForUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task AddMemberAsync(ProjectMember member, CancellationToken cancellationToken);
    Task RemoveMemberAsync(ProjectMember member, CancellationToken cancellationToken);

    Task<GovernanceRole?> GetGovernanceRoleByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<GovernanceRole>> ListGovernanceRolesAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddGovernanceRoleAsync(GovernanceRole role, CancellationToken cancellationToken);
    Task RemoveGovernanceRoleAsync(GovernanceRole role, CancellationToken cancellationToken);
}
