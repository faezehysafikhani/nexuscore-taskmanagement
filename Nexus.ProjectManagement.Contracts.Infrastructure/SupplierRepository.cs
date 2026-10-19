using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Domain;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

public sealed class SupplierRepository(ContractsDbContext dbContext) : ISupplierRepository
{
    public Task<Supplier?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Suppliers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Supplier>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Suppliers.Where(s => s.TenantId == tenantId).ToListAsync(cancellationToken);

    public Task<Supplier?> FindByNormalizedNameAsync(Guid tenantId, string normalizedName, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.Suppliers.FirstOrDefaultAsync(
            s => s.TenantId == tenantId && s.NormalizedName == normalizedName && (excludeId == null || s.Id != excludeId),
            cancellationToken);

    public async Task AddAsync(Supplier supplier, CancellationToken cancellationToken) =>
        await dbContext.Suppliers.AddAsync(supplier, cancellationToken);

    public Task RemoveAsync(Supplier supplier, CancellationToken cancellationToken)
    {
        dbContext.Suppliers.Remove(supplier);
        return Task.CompletedTask;
    }

    public Task<SupplierEvaluation?> GetEvaluationAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.SupplierEvaluations.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SupplierEvaluation>> ListEvaluationsAsync(Guid supplierId, CancellationToken cancellationToken) =>
        await dbContext.SupplierEvaluations.Where(e => e.SupplierId == supplierId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SupplierEvaluation>> ListTenantEvaluationsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.SupplierEvaluations.Where(e => e.TenantId == tenantId).ToListAsync(cancellationToken);

    public async Task AddEvaluationAsync(SupplierEvaluation evaluation, CancellationToken cancellationToken) =>
        await dbContext.SupplierEvaluations.AddAsync(evaluation, cancellationToken);

    public Task RemoveEvaluationAsync(SupplierEvaluation evaluation, CancellationToken cancellationToken)
    {
        dbContext.SupplierEvaluations.Remove(evaluation);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Contract>> ListContractsAsync(Guid tenantId, Guid? supplierId, CancellationToken cancellationToken) =>
        await dbContext.Contracts
            .Where(c => c.TenantId == tenantId && (supplierId == null || c.SupplierId == supplierId))
            .ToListAsync(cancellationToken);
}
