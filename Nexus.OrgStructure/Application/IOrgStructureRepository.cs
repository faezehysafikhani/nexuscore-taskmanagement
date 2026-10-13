using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>This module's own unit of work (see IOrganizationUnitOfWork for why each module has one).</summary>
public interface IOrgStructureUnitOfWork : IUnitOfWork;

public interface IOrgStructureRepository
{
    Task<IReadOnlyList<Company>> ListCompaniesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Company?> GetCompanyAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<bool> CompanyCodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken cancellationToken);
    Task AddCompanyAsync(Company company, CancellationToken cancellationToken);
    void RemoveCompany(Company company);

    Task<IReadOnlyList<CompanyUnit>> ListCompanyUnitsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<CompanyUnit?> GetCompanyUnitAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken);
    Task AddCompanyUnitAsync(CompanyUnit link, CancellationToken cancellationToken);
    void RemoveCompanyUnit(CompanyUnit link);

    Task<IReadOnlyList<Position>> ListPositionsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Position?> GetPositionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<bool> PositionTitleExistsAsync(Guid tenantId, string title, Guid? excludeId, CancellationToken cancellationToken);
    Task AddPositionAsync(Position position, CancellationToken cancellationToken);
    void RemovePosition(Position position);

    Task<IReadOnlyList<UserAssignment>> ListAssignmentsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<UserAssignment?> GetAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task AddAssignmentAsync(UserAssignment assignment, CancellationToken cancellationToken);
    void RemoveAssignment(UserAssignment assignment);
}
