using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>"TaskId cannot be completed before BlockedByTaskId is": a dependency between two tasks of the same tenant.</summary>
public sealed class TaskDependency : AuditableEntity<Guid>
{
    private TaskDependency() : base(Guid.Empty)
    {
    }

    public TaskDependency(Guid id, Guid tenantId, Guid taskId, Guid blockedByTaskId) : base(id)
    {
        TenantId = tenantId;
        TaskId = taskId;
        BlockedByTaskId = blockedByTaskId;
    }

    public Guid TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid BlockedByTaskId { get; private set; }
}

/// <summary>An acceptance criterion ("معیار پذیرش"): the task cannot be completed while one is not met.</summary>
public sealed class TaskCriterion : AuditableEntity<Guid>
{
    private TaskCriterion() : base(Guid.Empty)
    {
        Text = string.Empty;
    }

    public TaskCriterion(Guid id, Guid tenantId, Guid taskId, string text, int sortOrder) : base(id)
    {
        TenantId = tenantId;
        TaskId = taskId;
        Text = text.Trim();
        SortOrder = sortOrder;
    }

    public Guid TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public string Text { get; private set; }
    public bool IsMet { get; private set; }
    public Guid? MetByUserId { get; private set; }
    public DateTimeOffset? MetAtUtc { get; private set; }
    public int SortOrder { get; private set; }

    public void Rename(string text) => Text = text.Trim();

    public void SetMet(bool met, Guid? byUserId, DateTimeOffset nowUtc)
    {
        IsMet = met;
        MetByUserId = met ? byUserId : null;
        MetAtUtc = met ? nowUtc : null;
    }
}

/// <summary>The most tasks one person may have in progress at once (null limit = off). One row per tenant.</summary>
public sealed class TaskWipSetting
{
    private TaskWipSetting()
    {
    }

    public TaskWipSetting(Guid tenantId, int? limitPerUser)
    {
        TenantId = tenantId;
        LimitPerUser = limitPerUser;
    }

    public Guid TenantId { get; private set; }
    public int? LimitPerUser { get; private set; }

    public void Set(int? limitPerUser) => LimitPerUser = limitPerUser;
}

/// <summary>A reusable task or project: Body is a TaskTemplateBody as JSON.</summary>
public sealed class TaskTemplate : AuditableEntity<Guid>
{
    private TaskTemplate() : base(Guid.Empty)
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
        Body = "{}";
    }

    public TaskTemplate(Guid id, Guid tenantId, string name, bool isProject, string body) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        IsProject = isProject;
        Body = body;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public bool IsProject { get; private set; }
    public string Body { get; private set; }

    public void Update(string name, bool isProject, string body)
    {
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        IsProject = isProject;
        Body = body;
    }
}
