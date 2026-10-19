using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public interface ISupplierRepository
{
    Task<Supplier?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Supplier>> ListAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>The tenant's supplier whose normalised name is this one (see <see cref="SupplierNames"/>), other than <paramref name="excludeId"/>.</summary>
    Task<Supplier?> FindByNormalizedNameAsync(Guid tenantId, string normalizedName, Guid? excludeId, CancellationToken cancellationToken);

    Task AddAsync(Supplier supplier, CancellationToken cancellationToken);
    Task RemoveAsync(Supplier supplier, CancellationToken cancellationToken);

    /// <summary>Every contract of the tenant, or only those with this supplier.</summary>
    Task<IReadOnlyList<Contract>> ListContractsAsync(Guid tenantId, Guid? supplierId, CancellationToken cancellationToken);

    Task<SupplierEvaluation?> GetEvaluationAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupplierEvaluation>> ListEvaluationsAsync(Guid supplierId, CancellationToken cancellationToken);

    /// <summary>Every evaluation of the tenant's suppliers.</summary>
    Task<IReadOnlyList<SupplierEvaluation>> ListTenantEvaluationsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AddEvaluationAsync(SupplierEvaluation evaluation, CancellationToken cancellationToken);
    Task RemoveEvaluationAsync(SupplierEvaluation evaluation, CancellationToken cancellationToken);
}

public interface ISupplierService
{
    Task<Result<IReadOnlyList<SupplierDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<SupplierDetailDto>> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<Result<SupplierDto>> CreateAsync(Guid tenantId, CreateSupplierRequest request, CancellationToken cancellationToken);
    Task<Result<SupplierDto>> UpdateAsync(Guid tenantId, Guid id, UpdateSupplierRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
}
