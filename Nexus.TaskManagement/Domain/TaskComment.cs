using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>
/// A comment on a task - the discussion thread in the UI's task detail modal.
///
/// Added after the original entity list because the UI genuinely has the feature and nothing
/// in NexusCore covers it: AuditLog records what the system did, not what a person wrote, and
/// it has no editable body.
///
/// Only tasks carry comments. The UI's TaskComment always has a taskId and the comment box
/// only appears on the task detail modal, never on a subtask row, so there is deliberately
/// no SubTaskId here.
///
/// Attachments on comments are not modelled: the UI keeps them as inline base64 on the comment
/// object and never uploads them separately, so there is nothing for the backend to own yet.
/// </summary>
public sealed class TaskComment : AuditableEntity<Guid>
{
    private TaskComment() : base(Guid.Empty)
    {
        Text = string.Empty;
    }

    public TaskComment(Guid id, Guid tenantId, Guid taskId, Guid userId, string text) : base(id)
    {
        TenantId = tenantId;
        TaskId = taskId;
        UserId = userId;
        Text = text.Trim();
    }

    public Guid TenantId { get; private set; }

    public Guid TaskId { get; private set; }
    public TaskItem? Task { get; private set; }

    /// <summary>The author. Real foreign key into the shared identity schema.</summary>
    public Guid UserId { get; private set; }
    public User? User { get; private set; }

    public string Text { get; private set; }

    public void UpdateText(string text) => Text = text.Trim();
}
