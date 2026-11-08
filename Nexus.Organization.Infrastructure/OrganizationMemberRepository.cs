using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;

namespace Nexus.Organization.Infrastructure;

public sealed class OrganizationMemberRepository(OrganizationDbContext dbContext) : IOrganizationMemberRepository
{
    public Task<OrganizationUnitMember?> GetByUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.OrganizationUnitMembers.SingleOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<OrganizationUnitMember>> ListAsync(Guid tenantId, Guid? unitId, CancellationToken cancellationToken) =>
        await dbContext.OrganizationUnitMembers
            .Where(m => m.TenantId == tenantId && (unitId == null || m.UnitId == unitId))
            .ToListAsync(cancellationToken);

    public async Task AddAsync(OrganizationUnitMember member, CancellationToken cancellationToken) =>
        await dbContext.OrganizationUnitMembers.AddAsync(member, cancellationToken);

    public Task RemoveAsync(OrganizationUnitMember member, CancellationToken cancellationToken)
    {
        dbContext.OrganizationUnitMembers.Remove(member);
        return Task.CompletedTask;
    }
}
