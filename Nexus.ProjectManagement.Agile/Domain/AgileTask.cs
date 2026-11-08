using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Agile.Domain;

/// <summary>Named AgileTask, not Task, to avoid colliding with System.Threading.Tasks.Task -
/// every method in this codebase returns Task/Task&lt;T&gt;.</summary>
/// <summary>Stored as its integer value, so UnderReview was appended rather than inserted: on the
/// board it sits between InProgress and Done, but its value is 3. Never renumber the members.</summary>
public enum AgileTaskStatus { ToDo = 0, InProgress = 1, Done = 2, UnderReview = 3 }

public enum AgileTaskPriority { Low, Medium, High, Critical }

public sealed class AgileTask : AuditableEntity<Guid>
{
    private AgileTask() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public AgileTask(Guid id, Guid tenantId, Guid projectId, string title) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Title = title.Trim();
        Status = AgileTaskStatus.ToDo;
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public AgileTaskStatus Status { get; private set; }
    public Guid? ResponsibleUserId { get; private set; }
    public Guid? ApproverUserId { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public AgileTaskPriority Priority { get; private set; }
    public int? SprintNumber { get; private set; }

    /// <summary>Size estimate in story points; null = not estimated yet.</summary>
    public int? StoryPoints { get; private set; }

    /// <summary>Position among the project's tasks that share a status (lowest first): the order of
    /// a Kanban column, and of the backlog. Kept contiguous from 0 by the board when cards move.</summary>
    public int Rank { get; private set; }

    public ApprovalStatus ApprovalStatus { get; private set; }

    public void UpdateDetails(
        string title, string? description, Guid? responsibleUserId, Guid? approverUserId,
        DateOnly? dueDate, AgileTaskPriority priority, int? sprintNumber)
    {
        Title = title.Trim();
        Description = description;
        ResponsibleUserId = responsibleUserId;
        ApproverUserId = approverUserId;
        DueDate = dueDate;
        Priority = priority;
        SprintNumber = sprintNumber;
    }

    public void ChangeStatus(AgileTaskStatus status) => Status = status;

    /// <summary>Puts the task in a sprint (or back in the backlog with null). Done on its own so that
    /// carrying work over does not have to restate everything else about the task.</summary>
    public void AssignSprint(int? sprintNumber) => SprintNumber = sprintNumber;

    public void SetStoryPoints(int? storyPoints) => StoryPoints = storyPoints;

    public void SetRank(int rank) => Rank = rank;

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
