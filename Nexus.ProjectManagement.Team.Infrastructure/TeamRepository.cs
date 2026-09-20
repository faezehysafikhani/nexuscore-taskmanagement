using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Team.Application;
using Nexus.ProjectManagement.Team.Domain;

namespace Nexus.ProjectManagement.Team.Infrastructure;

public sealed class TeamRepository(TeamDbContext dbContext) : ITeamRepository
{
    public Task<ProjectMember?> GetMemberByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProjectMembers.SingleOrDefaultAsync(member => member.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ProjectMembers.Where(member => member.ProjectId == projectId).ToListAsync(cancellationToken);

    public Task<bool> IsMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.ProjectMembers.AnyAsync(member => member.ProjectId == projectId && member.UserId == userId, cancellationToken);

    public async Task AddMemberAsync(ProjectMember member, CancellationToken cancellationToken)
    {
        await dbContext.ProjectMembers.AddAsync(member, cancellationToken);
    }

    public Task RemoveMemberAsync(ProjectMember member, CancellationToken cancellationToken)
    {
        dbContext.ProjectMembers.Remove(member);
        return Task.CompletedTask;
    }

    public Task<GovernanceRole?> GetGovernanceRoleByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.GovernanceRoles.SingleOrDefaultAsync(role => role.Id == id, cancellationToken);

    public async Task<IReadOnlyList<GovernanceRole>> ListGovernanceRolesAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.GovernanceRoles.Where(role => role.ProjectId == projectId).ToListAsync(cancellationToken);

    public async Task AddGovernanceRoleAsync(GovernanceRole role, CancellationToken cancellationToken)
    {
        await dbContext.GovernanceRoles.AddAsync(role, cancellationToken);
    }
}
