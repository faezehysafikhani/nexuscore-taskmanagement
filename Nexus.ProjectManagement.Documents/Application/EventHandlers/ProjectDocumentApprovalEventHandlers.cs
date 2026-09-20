using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Documents.Application.EventHandlers;

public sealed class ProjectDocumentApprovalGrantedHandler(IProjectDocumentRepository repository, IDocumentsUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "ProjectDocument")
        {
            return;
        }

        var document = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (document is null)
        {
            return;
        }

        document.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ProjectDocumentApprovalRejectedHandler(IProjectDocumentRepository repository, IDocumentsUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "ProjectDocument")
        {
            return;
        }

        var document = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (document is null)
        {
            return;
        }

        document.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
