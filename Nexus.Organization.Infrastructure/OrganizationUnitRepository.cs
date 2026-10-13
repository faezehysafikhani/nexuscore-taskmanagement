using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;

namespace Nexus.Organization.Infrastructure;

public sealed class OrganizationUnitRepository(OrganizationDbContext dbContext) : IOrganizationUnitRepository
{
    public Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.OrganizationUnits.SingleOrDefaultAsync(unit => unit.Id == id, cancellationToken);

    public async Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.OrganizationUnits
            .Where(unit => unit.TenantId == tenantId)
            .OrderBy(unit => unit.Name)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(OrganizationUnit unit, CancellationToken cancellationToken)
    {
        await dbContext.OrganizationUnits.AddAsync(unit, cancellationToken);
    }
}
