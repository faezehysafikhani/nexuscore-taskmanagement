using Nexus.Workflow.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.Workflow.Application;

/// <summary>
/// Registered by AddWorkflowApplication() to replace NexusCore's NullApprovalRequester -
/// see NexusCore.Infrastructure.Approvals.NullApprovalRequester. Every capability that submits
/// an ApprovalSubject gets routed here uniformly; Workflow does not know or care which module
/// submitted it.
/// </summary>
public sealed class WorkflowApprovalRequester(
    IWorkflowDefinitionRepository definitionRepository,
    IWorkflowInstanceRepository instanceRepository,
    IWorkflowUnitOfWork unitOfWork) : IApprovalRequester
{
    public async Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken)
    {
        var definition = await definitionRepository.FindApplicableAsync(
            subject.TenantId, subject.SubjectType, subject.ScopeType, subject.ScopeId, cancellationToken);

        if (definition is null || definition.Steps.Count == 0)
        {
            return ApprovalRequestOutcome.NotConfigured;
        }

        var instance = new WorkflowInstance(Guid.NewGuid(), subject.TenantId, definition.Id, subject.SubjectType, subject.SubjectId, definition.Steps.Count);
        await instanceRepository.AddAsync(instance, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ApprovalRequestOutcome.Submitted;
    }
}
