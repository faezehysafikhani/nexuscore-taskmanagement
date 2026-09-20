using Nexus.ProjectManagement.Team.Domain;

namespace Nexus.ProjectManagement.Team.Application;

public interface ITeamRepository
{
    Task<ProjectMember?> GetMemberByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken);
    Task<bool> IsMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken);
    Task AddMemberAsync(ProjectMember member, CancellationToken cancellationToken);
    Task RemoveMemberAsync(ProjectMember member, CancellationToken cancellationToken);

    Task<GovernanceRole?> GetGovernanceRoleByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<GovernanceRole>> ListGovernanceRolesAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddGovernanceRoleAsync(GovernanceRole role, CancellationToken cancellationToken);
}
