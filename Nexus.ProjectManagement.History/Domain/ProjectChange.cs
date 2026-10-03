using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.History.Domain;

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum ProjectChangeKind
{
    Added = 0,
    Modified = 1,
    Deleted = 2
}

/// <summary>
/// One line of a project's change history: someone created, edited or deleted something that belongs to the
/// project (the project itself, a risk, an activity, a document...). Written once, never edited - hence a plain
/// Entity with its own timestamp and user rather than an AuditableEntity. ChangesJson is the list of changed
/// properties with their old and new values, as JSON text.
/// </summary>
public sealed class ProjectChange : Entity<Guid>
{
    private ProjectChange() : base(Guid.Empty)
    {
        EntityName = string.Empty;
    }

    public ProjectChange(
        Guid id, Guid? tenantId, Guid projectId, string entityName, Guid? entityId, ProjectChangeKind kind,
        Guid? changedByUserId, DateTimeOffset changedAtUtc, string? changesJson) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        EntityName = entityName;
        EntityId = entityId;
        Kind = kind;
        ChangedByUserId = changedByUserId;
        ChangedAtUtc = changedAtUtc;
        ChangesJson = changesJson;
    }

    public Guid? TenantId { get; private set; }
    public Guid ProjectId { get; private set; }

    /// <summary>The short type name of what changed: "Project", "Risk", "Activity"...</summary>
    public string EntityName { get; private set; }

    public Guid? EntityId { get; private set; }
    public ProjectChangeKind Kind { get; private set; }
    public Guid? ChangedByUserId { get; private set; }
    public DateTimeOffset ChangedAtUtc { get; private set; }
    public string? ChangesJson { get; private set; }
}
