using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Agile.Domain;

/// <summary>One line of a task's checklist - the small steps (sub-tasks) shown on its Kanban card.</summary>
public sealed class AgileChecklistItem : AuditableEntity<Guid>
{
    private AgileChecklistItem() : base(Guid.Empty)
    {
        Text = string.Empty;
    }

    public AgileChecklistItem(Guid id, Guid tenantId, Guid taskId, string text, int order) : base(id)
    {
        TenantId = tenantId;
        TaskId = taskId;
        Text = text.Trim();
        Order = order;
    }

    public Guid TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public string Text { get; private set; }
    public bool IsDone { get; private set; }

    /// <summary>Position within the task's checklist, lowest first.</summary>
    public int Order { get; private set; }

    public void Update(string text, bool isDone)
    {
        Text = text.Trim();
        IsDone = isDone;
    }
}
