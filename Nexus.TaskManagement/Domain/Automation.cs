using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

public enum AutomationTrigger
{
    TaskCreated = 0,
    StatusChanged = 1,
    SlaBreached = 2
}

public enum AutomationActionType
{
    SetPriority = 0,
    SetStatus = 1,
    AssignUser = 2,

    /// <summary>Give the task to the manager of whoever is responsible for it (needs the organization structure).</summary>
    AssignManager = 3,
    AddTag = 4,

    /// <summary>Start the SLA clock: due in Hours (counting only working days when BusinessDaysOnly).</summary>
    StartSla = 5,

    /// <summary>Tell UserId - or the task's owner when none is given.</summary>
    Notify = 6
}

/// <summary>When all given conditions hold (an absent one always does), the rule's actions run.</summary>
public sealed record AutomationConditions(TaskPriority? Priority = null, TaskItemStatus? Status = null, bool? IsProject = null, string? TitleContains = null);

public sealed record AutomationAction(
    AutomationActionType Type,
    TaskPriority? Priority = null,
    TaskItemStatus? Status = null,
    Guid? UserId = null,
    string? Tag = null,
    int? Hours = null,
    bool BusinessDaysOnly = false,
    string? Message = null);

/// <summary>Trigger + conditions + actions. Conditions and Actions are JSON (see <see cref="AutomationConditions"/>, <see cref="AutomationAction"/>).</summary>
public sealed class AutomationRule : AuditableEntity<Guid>
{
    private AutomationRule() : base(Guid.Empty)
    {
        Name = string.Empty;
        Conditions = "{}";
        Actions = "[]";
    }

    public AutomationRule(Guid id, Guid tenantId, string name, AutomationTrigger trigger, string conditions, string actions, int sortOrder) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        Trigger = trigger;
        Conditions = conditions;
        Actions = actions;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public AutomationTrigger Trigger { get; private set; }
    public string Conditions { get; private set; }
    public string Actions { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, AutomationTrigger trigger, string conditions, string actions, int sortOrder, bool isActive)
    {
        Name = name.Trim();
        Trigger = trigger;
        Conditions = conditions;
        Actions = actions;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

/// <summary>What a rule did (or why it could not) - the execution log.</summary>
public sealed class AutomationLogEntry
{
    private AutomationLogEntry()
    {
        RuleName = string.Empty;
        Outcome = string.Empty;
    }

    public AutomationLogEntry(Guid id, Guid tenantId, Guid ruleId, string ruleName, Guid taskId, AutomationTrigger trigger, string outcome, string? detail, DateTimeOffset atUtc)
    {
        Id = id;
        TenantId = tenantId;
        RuleId = ruleId;
        RuleName = ruleName;
        TaskId = taskId;
        Trigger = trigger;
        Outcome = outcome;
        Detail = detail;
        AtUtc = atUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RuleId { get; private set; }
    public string RuleName { get; private set; }
    public Guid TaskId { get; private set; }
    public AutomationTrigger Trigger { get; private set; }

    /// <summary>"Applied" or "Failed".</summary>
    public string Outcome { get; private set; }
    public string? Detail { get; private set; }
    public DateTimeOffset AtUtc { get; private set; }
}
