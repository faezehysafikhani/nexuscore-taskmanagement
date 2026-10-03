using Nexus.ProjectManagement.Contracts.Domain;

namespace Nexus.ProjectManagement.Contracts.Application;

public interface IContractRepository
{
    Task<Contract?> GetContractAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Contract>> ListContractsAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Whether the project already has a contract with this (digit-normalised) number.</summary>
    Task<bool> ContractNumberExistsAsync(Guid projectId, string contractNumber, Guid? excludeContractId, CancellationToken cancellationToken);

    Task AddContractAsync(Contract contract, CancellationToken cancellationToken);
    Task RemoveContractAsync(Contract contract, CancellationToken cancellationToken);

    Task<ContractAddendum?> GetAddendumAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContractAddendum>> ListAddendaAsync(Guid contractId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContractAddendum>> ListAddendaAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);
    Task AddAddendumAsync(ContractAddendum addendum, CancellationToken cancellationToken);
    Task RemoveAddendaAsync(IReadOnlyCollection<ContractAddendum> addenda, CancellationToken cancellationToken);

    Task<ContractInvoice?> GetInvoiceAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContractInvoice>> ListInvoicesAsync(Guid contractId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContractInvoice>> ListInvoicesAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);
    Task<bool> InvoiceNumberExistsAsync(Guid contractId, string invoiceNumber, Guid? excludeInvoiceId, CancellationToken cancellationToken);
    Task AddInvoiceAsync(ContractInvoice invoice, CancellationToken cancellationToken);
    Task RemoveInvoiceAsync(ContractInvoice invoice, CancellationToken cancellationToken);
}
