using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>
/// A personal note. Owned by exactly one user, and only ever readable by that user - the
/// service filters on the caller's own id, there is no sharing in the UI.
///
/// UserId is a real foreign key into the shared identity schema, matching the UI's
/// PersonalNote.userId.
/// </summary>
public sealed class Note : AuditableEntity<Guid>
{
    private Note() : base(Guid.Empty)
    {
        Title = string.Empty;
        Content = string.Empty;
    }

    public Note(Guid id, Guid tenantId, Guid userId, string title, string content, string? color = null)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Title = title.Trim();
        Content = content;
        Color = color;
        IsPinned = false;
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }

    public string Title { get; private set; }
    public string Content { get; private set; }

    /// <summary>UI palette key: amber | indigo | emerald | rose | cyan | slate.</summary>
    public string? Color { get; private set; }

    public bool IsPinned { get; private set; }

    public void Update(string title, string content, string? color)
    {
        Title = title.Trim();
        Content = content;
        Color = color;
    }

    public void SetPinned(bool isPinned) => IsPinned = isPinned;
}

/// <summary>
/// One of the extra users a task is shared with - the UI's teamMemberIds. The single main
/// assignee stays on <see cref="TaskItem.AssignedUserId"/>; this is the collaborator list.
/// </summary>
public sealed class TaskAssignee : Entity<Guid>
{
    private TaskAssignee() : base(Guid.Empty)
    {
    }

    public TaskAssignee(Guid id, Guid taskId, Guid userId) : base(id)
    {
        TaskId = taskId;
        UserId = userId;
    }

    public Guid TaskId { get; private set; }
    public TaskItem? Task { get; private set; }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }
}
