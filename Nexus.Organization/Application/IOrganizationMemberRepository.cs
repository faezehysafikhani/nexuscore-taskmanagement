using Nexus.Organization.Domain;

namespace Nexus.Organization.Application;

public interface IOrganizationMemberRepository
{
    Task<OrganizationUnitMember?> GetByUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationUnitMember>> ListAsync(Guid tenantId, Guid? unitId, CancellationToken cancellationToken);
    Task AddAsync(OrganizationUnitMember member, CancellationToken cancellationToken);
    Task RemoveAsync(OrganizationUnitMember member, CancellationToken cancellationToken);
}
