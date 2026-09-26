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
/// A user a task is shared with - the task's access list (the UI's teamMemberIds). Those marked
/// <see cref="IsResponsible"/> are the people responsible for doing it (one or more); the first
/// of them is also kept on <see cref="TaskItem.AssignedUserId"/> for older clients.
/// </summary>
public sealed class TaskAssignee : Entity<Guid>
{
    private TaskAssignee() : base(Guid.Empty)
    {
    }

    public TaskAssignee(Guid id, Guid taskId, Guid userId, bool isResponsible = false) : base(id)
    {
        TaskId = taskId;
        UserId = userId;
        IsResponsible = isResponsible;
    }

    /// <summary>Responsible for doing the task, not only allowed to see and work on it.</summary>
    public bool IsResponsible { get; private set; }

    internal void SetResponsible(bool isResponsible) => IsResponsible = isResponsible;

    public Guid TaskId { get; private set; }
    public TaskItem? Task { get; private set; }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }
}
