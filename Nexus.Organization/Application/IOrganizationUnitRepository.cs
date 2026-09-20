using Nexus.Organization.Domain;

namespace Nexus.Organization.Application;

public interface IOrganizationUnitRepository
{
    Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken cancellationToken);
    Task AddAsync(OrganizationUnit unit, CancellationToken cancellationToken);
}
