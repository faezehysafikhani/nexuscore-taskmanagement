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
    IWorkflowUnitOfWork unitOfWork,
    IEnumerable<IApprovalParticipantResolver>? participantResolvers = null) : IApprovalRequester
{
    public async Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken)
    {
        var definition = await definitionRepository.FindApplicableAsync(
            subject.TenantId, subject.SubjectType, subject.ScopeType, subject.ScopeId, cancellationToken);

        if (definition is null || definition.Steps.Count == 0)
        {
            return ApprovalRequestOutcome.NotConfigured;
        }

        // The people each step points at are fixed now, when the approval starts.
        var participants = await ResolveParticipantsAsync(subject, cancellationToken);
        var steps = new List<WorkflowInstanceStep>();
        foreach (var step in definition.Steps.OrderBy(s => s.Order))
        {
            var approver = step.ApproverUserId ?? Resolve(participants, step.ApproverKey);
            if (approver is null && step.ApproverKey is not null && step.IsOptional)
            {
                continue;
            }

            steps.Add(new WorkflowInstanceStep(steps.Count + 1, step.Name, approver));
        }

        // Every step was optional and none applies: nothing to approve, the submitter's own rule decides.
        if (steps.Count == 0)
        {
            return ApprovalRequestOutcome.NotConfigured;
        }

        var instance = new WorkflowInstance(Guid.NewGuid(), subject.TenantId, definition.Id, subject.SubjectType, subject.SubjectId, steps);
        await instanceRepository.AddAsync(instance, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ApprovalRequestOutcome.Submitted;
    }

    private async Task<Dictionary<string, Guid>> ResolveParticipantsAsync(ApprovalSubject subject, CancellationToken cancellationToken)
    {
        var participants = new Dictionary<string, Guid>();
        foreach (var resolver in participantResolvers ?? [])
        {
            foreach (var (key, userId) in await resolver.ResolveAsync(subject, cancellationToken))
            {
                participants.TryAdd(key, userId);
            }
        }

        foreach (var (key, userId) in subject.Participants ?? new Dictionary<string, Guid>())
        {
            participants.TryAdd(key, userId);
        }

        return participants;
    }

    private static Guid? Resolve(Dictionary<string, Guid> participants, string? key)
    {
        while (key is not null)
        {
            if (participants.TryGetValue(key, out var userId))
            {
                return userId;
            }

            key = ApprovalParticipants.FallbackOf(key);
        }

        return null;
    }
}
