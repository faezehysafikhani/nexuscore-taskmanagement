using Nexus.ProjectManagement.Contracts.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public interface IContractService
{
    Task<Result<IReadOnlyList<ContractDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<ContractDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ProjectContractsSummaryDto>> GetProjectSummaryAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<ContractDto>> CreateAsync(CreateContractRequest request, CancellationToken cancellationToken);
    Task<Result<ContractDto>> UpdateAsync(Guid id, UpdateContractRequest request, CancellationToken cancellationToken);
    Task<Result<ContractDto>> ChangeStatusAsync(Guid id, ChangeContractStatusRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ContractDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}

public interface IContractAddendumService
{
    Task<Result<IReadOnlyList<ContractAddendumDto>>> ListAsync(Guid contractId, CancellationToken cancellationToken);
    Task<Result<ContractAddendumDto>> CreateAsync(Guid contractId, CreateContractAddendumRequest request, CancellationToken cancellationToken);
    Task<Result<ContractAddendumDto>> UpdateAsync(Guid contractId, Guid addendumId, UpdateContractAddendumRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid contractId, Guid addendumId, CancellationToken cancellationToken);
    Task<Result<ContractAddendumDto>> SubmitForApprovalAsync(Guid contractId, Guid addendumId, CancellationToken cancellationToken);
}

public interface IContractInvoiceService
{
    Task<Result<IReadOnlyList<ContractInvoiceDto>>> ListAsync(Guid contractId, CancellationToken cancellationToken);
    Task<Result<ContractInvoiceDto>> CreateAsync(Guid contractId, CreateContractInvoiceRequest request, CancellationToken cancellationToken);
    Task<Result<ContractInvoiceDto>> UpdateAsync(Guid contractId, Guid invoiceId, UpdateContractInvoiceRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid contractId, Guid invoiceId, CancellationToken cancellationToken);
    Task<Result<ContractInvoiceDto>> SubmitForApprovalAsync(Guid contractId, Guid invoiceId, CancellationToken cancellationToken);
    Task<Result<ContractInvoiceDto>> RecordPaymentAsync(Guid contractId, Guid invoiceId, RecordInvoicePaymentRequest request, CancellationToken cancellationToken);
}
