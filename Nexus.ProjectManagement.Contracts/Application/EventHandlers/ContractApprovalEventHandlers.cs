using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Contracts.Application.EventHandlers;

/// <summary>Applies the Workflow decision to a contract, an addendum or an invoice, by SubjectType.</summary>
public sealed class ContractApprovalGrantedHandler(IContractRepository repository, IContractsUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        switch (domainEvent.SubjectType)
        {
            case ContractService.SubjectType:
                (await repository.GetContractAsync(domainEvent.SubjectId, cancellationToken))?.Approve();
                break;
            case ContractAddendumService.SubjectType:
                (await repository.GetAddendumAsync(domainEvent.SubjectId, cancellationToken))?.Approve();
                break;
            case ContractInvoiceService.SubjectType:
                (await repository.GetInvoiceAsync(domainEvent.SubjectId, cancellationToken))?.Approve();
                break;
            default:
                return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ContractApprovalRejectedHandler(IContractRepository repository, IContractsUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        switch (domainEvent.SubjectType)
        {
            case ContractService.SubjectType:
                (await repository.GetContractAsync(domainEvent.SubjectId, cancellationToken))?.Reject();
                break;
            case ContractAddendumService.SubjectType:
                (await repository.GetAddendumAsync(domainEvent.SubjectId, cancellationToken))?.Reject();
                break;
            case ContractInvoiceService.SubjectType:
                (await repository.GetInvoiceAsync(domainEvent.SubjectId, cancellationToken))?.Reject();
                break;
            default:
                return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
