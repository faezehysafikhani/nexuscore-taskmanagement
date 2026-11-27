using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>
/// A status the organization defines for its tasks ("در بازبینی", "منتظر مشتری" ...). Every one maps
/// to one of the four built-in statuses (<see cref="Category"/>): that is what reports, reminders
/// and "is it done" logic keep reading, so a custom status can never break them.
/// </summary>
public sealed class TaskStatusDefinition : AuditableEntity<Guid>
{
    private TaskStatusDefinition() : base(Guid.Empty)
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    public TaskStatusDefinition(Guid id, Guid tenantId, string name, TaskItemStatus category, string? color, int sortOrder) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Category = category;
        Color = color;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public TaskItemStatus Category { get; private set; }
    public string? Color { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>An inactive status stays on the tasks that have it but cannot be picked for more.</summary>
    public bool IsActive { get; private set; }

    public void Update(string name, TaskItemStatus category, string? color, int sortOrder, bool isActive)
    {
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Category = category;
        Color = color;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}
