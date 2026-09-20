using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>A reusable label. Unique per tenant by normalized name.</summary>
public sealed class Tag : AuditableEntity<Guid>
{
    private Tag() : base(Guid.Empty)
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    public Tag(Guid id, Guid tenantId, string name, string? color = null) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        NormalizedName = name.Trim().ToUpperInvariant();
        Color = color;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public string? Color { get; private set; }

    public void Rename(string name)
    {
        Name = name.Trim();
        NormalizedName = name.Trim().ToUpperInvariant();
    }

    public void SetColor(string? color) => Color = color;
}

/// <summary>
/// Links a tag to a task, a subtask, or both at once.
///
/// Unlike <see cref="TaskFile"/> this is deliberately *not* exclusive: a single row may carry
/// both TaskId and SubTaskId, and that row means the tag applies to both. Only the
/// "at least one is set" rule is enforced.
///
/// Because one row can serve two owners, detaching a tag from a task must null that one column
/// and keep the row when the other owner is still set - deleting the row would silently drop
/// the surviving link. <see cref="DetachTask"/> / <see cref="DetachSubTask"/> exist so callers
/// cannot get that wrong; <see cref="IsOrphaned"/> then says whether the row may be removed.
/// </summary>
public sealed class TaskTag : Entity<Guid>
{
    private TaskTag() : base(Guid.Empty)
    {
    }

    private TaskTag(Guid id, Guid tagId, Guid? taskId, Guid? subTaskId) : base(id)
    {
        TagId = tagId;
        TaskId = taskId;
        SubTaskId = subTaskId;
    }

    public Guid TagId { get; private set; }
    public Tag? Tag { get; private set; }

    public Guid? TaskId { get; private set; }
    public TaskItem? Task { get; private set; }

    public Guid? SubTaskId { get; private set; }
    public SubTask? SubTask { get; private set; }

    public static TaskTag ForTask(Guid id, Guid tagId, Guid taskId) => new(id, tagId, taskId, null);

    public static TaskTag ForSubTask(Guid id, Guid tagId, Guid subTaskId) => new(id, tagId, null, subTaskId);

    public static TaskTag ForBoth(Guid id, Guid tagId, Guid taskId, Guid subTaskId) => new(id, tagId, taskId, subTaskId);

    public void DetachTask() => TaskId = null;

    public void DetachSubTask() => SubTaskId = null;

    /// <summary>True once nothing is attached any more, so the caller may delete the row.</summary>
    public bool IsOrphaned => TaskId is null && SubTaskId is null;
}
