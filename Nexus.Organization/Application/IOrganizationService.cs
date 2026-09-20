using Nexus.Organization.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Organization.Application;

public interface IOrganizationService
{
    Task<Result<IReadOnlyList<OrganizationUnitDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<OrganizationUnitDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<OrganizationUnitDto>> CreateAsync(CreateOrganizationUnitRequest request, CancellationToken cancellationToken);
    Task<Result<OrganizationUnitDto>> UpdateAsync(Guid id, UpdateOrganizationUnitRequest request, CancellationToken cancellationToken);
    Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken);
}
