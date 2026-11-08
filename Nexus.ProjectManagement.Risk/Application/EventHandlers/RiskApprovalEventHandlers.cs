using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Application.EventHandlers;

public sealed class RiskApprovalGrantedHandler(IRiskRepository repository, IRiskUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "Risk")
        {
            return;
        }

        var risk = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (risk is null)
        {
            return;
        }

        risk.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class RiskApprovalRejectedHandler(IRiskRepository repository, IRiskUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != "Risk")
        {
            return;
        }

        var risk = await repository.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (risk is null)
        {
            return;
        }

        risk.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>A response report was verified: the risk takes the status that was reported.</summary>
public sealed class RiskResponseApprovalGrantedHandler(
    IRiskResponseSubmissionRepository submissions, IRiskService risks, IRiskUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalGranted>
{
    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != RiskResponseSubmissionService.SubjectType)
        {
            return;
        }

        var submission = await submissions.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (submission is null)
        {
            return;
        }

        submission.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await risks.ChangeResponseStatusAsync(submission.RiskId, new Dtos.ChangeRiskResponseStatusRequest(submission.SubmittedStatus), cancellationToken);
    }
}

public sealed class RiskResponseApprovalRejectedHandler(IRiskResponseSubmissionRepository submissions, IRiskUnitOfWork unitOfWork)
    : IDomainEventHandler<ApprovalRejected>
{
    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubjectType != RiskResponseSubmissionService.SubjectType)
        {
            return;
        }

        var submission = await submissions.GetByIdAsync(domainEvent.SubjectId, cancellationToken);
        if (submission is null)
        {
            return;
        }

        submission.Reject();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
