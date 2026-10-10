using Nexus.Organization.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Organization.Application;

public interface IOrganizationService
{
    Task<Result<IReadOnlyList<OrganizationUnitDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken, bool activeOnly = false);
    Task<Result<OrganizationUnitDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<OrganizationUnitDto>> CreateAsync(CreateOrganizationUnitRequest request, CancellationToken cancellationToken);
    Task<Result<OrganizationUnitDto>> UpdateAsync(Guid id, UpdateOrganizationUnitRequest request, CancellationToken cancellationToken);
    /// <summary>Deactivates the unit; with <paramref name="includeBranch"/> its sub-units too. Refused (409) while anyone is placed in them.</summary>
    Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken, bool includeBranch = false);
}
