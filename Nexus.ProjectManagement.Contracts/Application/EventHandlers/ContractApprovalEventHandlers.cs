using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Contracts.Application.EventHandlers;

/// <summary>Applies the Workflow decision to a contract, an addendum, an invoice or a change request, by SubjectType.</summary>
public sealed class ContractApprovalGrantedHandler(IContractRepository repository, IContractsUnitOfWork unitOfWork, IChangeRequestRepository? changes = null)
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
            case ChangeRequestService.SubjectType when changes is not null:
                (await changes.GetAsync(domainEvent.SubjectId, cancellationToken))?.Approve(DateTimeOffset.UtcNow, domainEvent.Comment);
                break;
            default:
                return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ContractApprovalRejectedHandler(IContractRepository repository, IContractsUnitOfWork unitOfWork, IChangeRequestRepository? changes = null)
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
            case ChangeRequestService.SubjectType when changes is not null:
                (await changes.GetAsync(domainEvent.SubjectId, cancellationToken))?.Reject(DateTimeOffset.UtcNow, domainEvent.Comment);
                break;
            default:
                return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
