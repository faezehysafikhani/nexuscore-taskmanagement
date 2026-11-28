using Nexus.Workflow.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.Workflow.Application;

/// <summary>Workflow's answer to <see cref="IApprovalTracker"/>: the newest instance of a subject, and withdrawing running ones.</summary>
public sealed class WorkflowApprovalTracker(IWorkflowInstanceRepository repository, IWorkflowUnitOfWork unitOfWork) : IApprovalTracker
{
    public async Task<ApprovalRunSnapshot?> GetLatestAsync(ApprovalSubject subject, CancellationToken cancellationToken)
    {
        var instances = await repository.ListForSubjectsAsync(subject.TenantId, subject.SubjectType, [subject.SubjectId], cancellationToken);
        // One still running wins; otherwise the newest (by start, then by its last decision).
        var latest = instances.FirstOrDefault(i => i.Status == WorkflowInstanceStatus.InProgress)
            ?? instances.OrderByDescending(i => i.CreatedAtUtc)
                .ThenByDescending(i => i.Decisions.Select(d => (DateTimeOffset?)d.DecidedAtUtc).Max())
                .FirstOrDefault();
        if (latest is null)
        {
            return null;
        }

        var lastDecision = latest.Decisions.OrderBy(d => d.DecidedAtUtc).LastOrDefault();
        var state = latest.Status switch
        {
            WorkflowInstanceStatus.Approved => ApprovalRunState.Approved,
            WorkflowInstanceStatus.Rejected => ApprovalRunState.Rejected,
            WorkflowInstanceStatus.Withdrawn => ApprovalRunState.Withdrawn,
            _ => ApprovalRunState.InProgress
        };
        return new ApprovalRunSnapshot(state, lastDecision?.Comment, lastDecision?.DecidedAtUtc);
    }

    public async Task<int> WithdrawAsync(ApprovalSubject subject, CancellationToken cancellationToken)
    {
        var instances = await repository.ListForSubjectsAsync(subject.TenantId, subject.SubjectType, [subject.SubjectId], cancellationToken);
        var withdrawn = instances.Count(instance => instance.Withdraw());
        if (withdrawn > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return withdrawn;
    }
}
