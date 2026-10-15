using Nexus.ProjectManagement.Core.Application;
using NexusCore.Application.Approvals;

namespace Nexus.Integrations.ProjectWorkflow.Application;

/// <summary>
/// Tells the approval backend who the Manager and the Owner of a subject are when the subject belongs to a
/// Project (or is the Project itself): the project's manager and owner. This is what lets a workflow step say
/// "the project manager" instead of one fixed user. Installed only with the integration.
/// </summary>
public sealed class ProjectApprovalParticipantResolver(IProjectRepository projectRepository) : IApprovalParticipantResolver
{
    public async Task<IReadOnlyDictionary<string, Guid>> ResolveAsync(ApprovalSubject subject, CancellationToken cancellationToken)
    {
        var projectId = subject.SubjectType == ProjectManagementSubjectTypes.Project
            ? subject.SubjectId
            : subject is { ScopeType: "Project", ScopeId: { } scopeId } ? scopeId : (Guid?)null;
        if (projectId is null)
        {
            return new Dictionary<string, Guid>();
        }

        var project = await projectRepository.GetByIdAsync(projectId.Value, cancellationToken);
        var participants = new Dictionary<string, Guid>();
        if (project?.ManagerUserId is { } manager)
        {
            participants[ApprovalParticipants.Manager] = manager;
        }

        if (project?.OwnerUserId is { } owner)
        {
            participants[ApprovalParticipants.Owner] = owner;
        }

        return participants;
    }
}
