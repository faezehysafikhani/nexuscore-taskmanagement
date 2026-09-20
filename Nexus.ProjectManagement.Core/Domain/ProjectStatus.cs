namespace Nexus.ProjectManagement.Core.Domain;

/// <summary>Lifecycle status - independent of NexusCore.Application.Approvals.ApprovalStatus.</summary>
public enum ProjectStatus
{
    Draft,
    Active,
    OnHold,
    Completed,
    Archived
}
