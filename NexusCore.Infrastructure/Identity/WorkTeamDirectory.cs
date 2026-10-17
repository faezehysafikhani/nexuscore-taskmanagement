using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Infrastructure.Persistence;

namespace NexusCore.Infrastructure.Identity;

public sealed class WorkTeamDirectory(NexusCoreDbContext dbContext) : IWorkTeamDirectory
{
    public async Task<IReadOnlyList<WorkTeamInfo>> ListWorkTeamsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await dbContext.UserGroups
            .AsNoTracking()
            .Where(group => group.TenantId == tenantId && group.IsActive && group.OwnerUserId != null)
            .Select(group => new
            {
                group.Id,
                group.Name,
                group.Description,
                OwnerUserId = group.OwnerUserId!.Value,
                MemberIds = group.Members.Select(member => member.UserId).ToList(),
            })
            .OrderBy(group => group.Name)
            .ToListAsync(cancellationToken))
        .Select(group => new WorkTeamInfo(group.Id, group.Name, group.Description, group.OwnerUserId, group.MemberIds))
        .ToList();
}
