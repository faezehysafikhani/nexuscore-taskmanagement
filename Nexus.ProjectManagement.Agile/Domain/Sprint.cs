using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Agile.Domain;

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum SprintStatus
{
    /// <summary>Being prepared; tasks can be added and removed freely.</summary>
    Planned = 0,
    /// <summary>Running. A project has at most one active sprint.</summary>
    Active = 1,
    /// <summary>Finished; its history (scope, completion) is kept and no task can be added to it.</summary>
    Completed = 2
}

/// <summary>
/// A time-boxed iteration. Tasks join it through <see cref="AgileTask.SprintNumber"/> (the number
/// has always been the link), so a task without a sprint is in the backlog. Sprints are numbered
/// 1, 2, 3... per project and the number never changes.
/// </summary>
public sealed class Sprint : AuditableEntity<Guid>
{
    private Sprint() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    public Sprint(Guid id, Guid tenantId, Guid projectId, int number, string name, string? goal, DateOnly? startDate, DateOnly? endDate) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Number = number;
        Name = name.Trim();
        Goal = string.IsNullOrWhiteSpace(goal) ? null : goal.Trim();
        StartDate = startDate;
        EndDate = endDate;
        Status = SprintStatus.Planned;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public int Number { get; private set; }
    public string Name { get; private set; }
    public string? Goal { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public SprintStatus Status { get; private set; }

    public void UpdateDetails(string name, string? goal, DateOnly? startDate, DateOnly? endDate)
    {
        Name = name.Trim();
        Goal = string.IsNullOrWhiteSpace(goal) ? null : goal.Trim();
        StartDate = startDate;
        EndDate = endDate;
    }

    public void Start() => Status = SprintStatus.Active;

    public void Complete() => Status = SprintStatus.Completed;
}
