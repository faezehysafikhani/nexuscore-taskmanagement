using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>A kind of work the organization names ("باگ", "درخواست تغییر" ...): a label on a task, apart from its status.</summary>
public sealed class WorkTypeDefinition : AuditableEntity<Guid>
{
    private WorkTypeDefinition() : base(Guid.Empty)
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    public WorkTypeDefinition(Guid id, Guid tenantId, string name, string? color, int sortOrder) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Color = color;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public string? Color { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string? color, int sortOrder, bool isActive)
    {
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Color = color;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

public enum CustomFieldKind
{
    Text = 0,
    Number = 1,
    Date = 2,
    Choice = 3,
    Boolean = 4,
}

/// <summary>An extra field the organization wants on every task ("شماره قرارداد", "مشتری" ...).</summary>
public sealed class CustomFieldDefinition : AuditableEntity<Guid>
{
    private CustomFieldDefinition() : base(Guid.Empty)
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    public CustomFieldDefinition(Guid id, Guid tenantId, string name, CustomFieldKind kind, string? optionsJson, bool isRequired, int sortOrder) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Kind = kind;
        OptionsJson = optionsJson;
        IsRequired = isRequired;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public CustomFieldKind Kind { get; private set; }

    /// <summary>For <see cref="CustomFieldKind.Choice"/>: the allowed values, as a JSON array of strings.</summary>
    public string? OptionsJson { get; private set; }
    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string? optionsJson, bool isRequired, int sortOrder, bool isActive)
    {
        // The kind never changes: values already stored would no longer fit it.
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        OptionsJson = optionsJson;
        IsRequired = isRequired;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

/// <summary>The value one task holds for one custom field (stored as text; the field's kind says how to read it).</summary>
public sealed class TaskFieldValue : Entity<Guid>
{
    private TaskFieldValue() : base(Guid.Empty)
    {
        Value = string.Empty;
    }

    public TaskFieldValue(Guid id, Guid tenantId, Guid taskId, Guid fieldId, string value) : base(id)
    {
        TenantId = tenantId;
        TaskId = taskId;
        FieldId = fieldId;
        Value = value;
    }

    public Guid TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid FieldId { get; private set; }
    public string Value { get; private set; }

    public void Set(string value) => Value = value;
}
