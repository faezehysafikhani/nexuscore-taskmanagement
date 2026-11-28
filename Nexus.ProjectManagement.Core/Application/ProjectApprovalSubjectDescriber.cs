using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Core.Application;

/// <summary>Names a project in messages about its approval (the notification of approvers and submitter).</summary>
public sealed class ProjectApprovalSubjectDescriber(IProjectRepository repository) : IApprovalSubjectDescriber
{
    public async Task<string?> DescribeAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken) =>
        subjectType == "Project" ? (await repository.GetByIdAsync(subjectId, cancellationToken))?.Name : null;
}
