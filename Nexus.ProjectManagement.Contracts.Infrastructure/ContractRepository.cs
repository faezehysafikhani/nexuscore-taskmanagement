using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Domain;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

public sealed class ContractRepository(ContractsDbContext dbContext) : IContractRepository
{
    public Task<Contract?> GetContractAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Contracts.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Contract>> ListContractsAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.Contracts.Where(c => c.ProjectId == projectId).ToListAsync(cancellationToken);

    public Task<bool> ContractNumberExistsAsync(Guid projectId, string contractNumber, Guid? excludeContractId, CancellationToken cancellationToken) =>
        dbContext.Contracts.AnyAsync(
            c => c.ProjectId == projectId && c.ContractNumber == contractNumber && (excludeContractId == null || c.Id != excludeContractId),
            cancellationToken);

    public async Task AddContractAsync(Contract contract, CancellationToken cancellationToken) =>
        await dbContext.Contracts.AddAsync(contract, cancellationToken);

    public Task RemoveContractAsync(Contract contract, CancellationToken cancellationToken)
    {
        dbContext.Contracts.Remove(contract);
        return Task.CompletedTask;
    }

    public Task<ContractAddendum?> GetAddendumAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ContractAddenda.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContractAddendum>> ListAddendaAsync(Guid contractId, CancellationToken cancellationToken) =>
        await dbContext.ContractAddenda.Where(a => a.ContractId == contractId).OrderBy(a => a.Number).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContractAddendum>> ListAddendaAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken) =>
        await dbContext.ContractAddenda.Where(a => contractIds.Contains(a.ContractId)).ToListAsync(cancellationToken);

    public async Task AddAddendumAsync(ContractAddendum addendum, CancellationToken cancellationToken) =>
        await dbContext.ContractAddenda.AddAsync(addendum, cancellationToken);

    public Task RemoveAddendaAsync(IReadOnlyCollection<ContractAddendum> addenda, CancellationToken cancellationToken)
    {
        dbContext.ContractAddenda.RemoveRange(addenda);
        return Task.CompletedTask;
    }

    public Task<ContractInvoice?> GetInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ContractInvoices.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContractInvoice>> ListInvoicesAsync(Guid contractId, CancellationToken cancellationToken) =>
        await dbContext.ContractInvoices.Where(i => i.ContractId == contractId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContractInvoice>> ListInvoicesAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken) =>
        await dbContext.ContractInvoices.Where(i => contractIds.Contains(i.ContractId)).ToListAsync(cancellationToken);

    public Task<bool> InvoiceNumberExistsAsync(Guid contractId, string invoiceNumber, Guid? excludeInvoiceId, CancellationToken cancellationToken) =>
        dbContext.ContractInvoices.AnyAsync(
            i => i.ContractId == contractId && i.InvoiceNumber == invoiceNumber && (excludeInvoiceId == null || i.Id != excludeInvoiceId),
            cancellationToken);

    public async Task AddInvoiceAsync(ContractInvoice invoice, CancellationToken cancellationToken) =>
        await dbContext.ContractInvoices.AddAsync(invoice, cancellationToken);

    public Task RemoveInvoiceAsync(ContractInvoice invoice, CancellationToken cancellationToken)
    {
        dbContext.ContractInvoices.Remove(invoice);
        return Task.CompletedTask;
    }
}
