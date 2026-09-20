using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Core.Application.EventHandlers;

/// <summary>
/// Closes the loop for the optional Workflow integration: when Workflow (or any other approval
/// backend) decides on a subject this module submitted, react by updating the Project - without
/// ever referencing Workflow. Every handler self-filters on SubjectType.
/// </summary>
public sealed class ProjectApprovalGrantedHandler(IProjectRepository repository, IProjectManagementUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "Project")
        {
            return;
        }

        var project = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (project is null)
        {
            return;
        }

        project.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ProjectApprovalRejectedHandler(IProjectRepository repository, IProjectManagementUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "Project")
        {
            return;
        }

        var project = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (project is null)
        {
            return;
        }

        project.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
