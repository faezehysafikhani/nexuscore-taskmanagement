using NexusCore.Application.Approvals;

namespace NexusCore.Infrastructure.Approvals;

/// <summary>
/// Default registration when no Workflow-aware module is installed. Every module that supports
/// optional approval integration must behave correctly against this implementation alone.
/// </summary>
public sealed class NullApprovalRequester : IApprovalRequester
{
    public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalRequestOutcome.NotConfigured);
}
