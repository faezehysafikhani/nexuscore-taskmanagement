using Nexus.Integrations.ProjectWorkflow.Application;
using Nexus.ProjectManagement.Contracts.Application;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>Change requests can be given an approval chain like the other project records.</summary>
public sealed class ChangeRequestApprovalsTests
{
    [Fact]
    public void TheSubjectTypeTheModuleSubmits_IsAKnownProjectSubjectType()
    {
        Assert.Equal(ChangeRequestService.SubjectType, ProjectManagementSubjectTypes.ChangeRequest);
        Assert.Contains(ProjectManagementSubjectTypes.ChangeRequest, ProjectManagementSubjectTypes.All);
    }

    [Fact]
    public void ChangeRequests_HaveTheirOwnGroup_ReviewedByTheManagerAndDecidedByTheOwner()
    {
        var group = ProjectWorkflowGroups.Find("change_requests");

        Assert.NotNull(group);
        Assert.Equal([ProjectManagementSubjectTypes.ChangeRequest], group!.SubjectTypes);
        Assert.Equal([ApprovalParticipants.Manager, ApprovalParticipants.Owner], group.Defaults.Select(s => s.ApproverKey));
        Assert.Single(ProjectWorkflowGroups.All, g => g.SubjectTypes.Contains(ProjectManagementSubjectTypes.ChangeRequest));
    }
}
