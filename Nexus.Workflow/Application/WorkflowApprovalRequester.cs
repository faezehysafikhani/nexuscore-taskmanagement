using Nexus.Workflow.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Interfaces;

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
    IEnumerable<IApprovalParticipantResolver>? participantResolvers = null,
    IEnumerable<IApprovalKeyResolver>? keyResolvers = null,
    ICurrentUserContext? currentUser = null) : IApprovalRequester
{
    public async Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken)
    {
        var definition = await definitionRepository.FindApplicableAsync(
            subject.TenantId, subject.SubjectType, subject.ScopeType, subject.ScopeId, cancellationToken);

        if (definition is null || definition.Steps.Count == 0)
        {
            return ApprovalRequestOutcome.NotConfigured;
        }

        // Sent again while its approval is still running (a double click, a resubmit): the running one stands, no second
        // approval is started - otherwise the same item would wait twice in the approvers' inboxes.
        if (await IsRunningAsync(subject, cancellationToken))
        {
            return ApprovalRequestOutcome.Submitted;
        }

        // The people each step points at are fixed now, when the approval starts.
        var participants = await ResolveParticipantsAsync(subject, cancellationToken);
        var steps = new List<WorkflowInstanceStep>();
        foreach (var step in definition.Steps.OrderBy(s => s.Order))
        {
            var approver = step.ApproverUserId ?? Resolve(participants, step.ApproverKey)
                ?? await ResolveKeyAsync(subject, step.ApproverKey, cancellationToken);
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

    private async Task<bool> IsRunningAsync(ApprovalSubject subject, CancellationToken cancellationToken)
    {
        try
        {
            var approvals = await instanceRepository.ListForSubjectsAsync(subject.TenantId, subject.SubjectType, [subject.SubjectId], cancellationToken);
            return approvals.Any(instance => instance.Status == WorkflowInstanceStatus.InProgress);
        }
        catch (NotSupportedException)
        {
            return false; // a repository that cannot list by subject: behave as before
        }
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

    /// <summary>A key only the organization chart can answer (the submitter's manager, a post, a position).</summary>
    private async Task<Guid?> ResolveKeyAsync(ApprovalSubject subject, string? key, CancellationToken cancellationToken)
    {
        if (key is null)
        {
            return null;
        }

        foreach (var resolver in keyResolvers ?? [])
        {
            if (await resolver.ResolveAsync(subject, currentUser?.UserId, key, cancellationToken) is { } userId)
            {
                return userId;
            }
        }

        return null;
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
