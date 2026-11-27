using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

public enum TaskRequestStatus
{
    New = 0,
    Converted = 1,
    Rejected = 2
}

/// <summary>
/// A kind of request ("پشتیبانی", "خرید" ...). The routing is simple on purpose: who gets it, and who gets it when it
/// is urgent (High priority). A routing by the manager chain comes with the automation phase.
/// </summary>
public sealed class RequestCategory : AuditableEntity<Guid>
{
    private RequestCategory() : base(Guid.Empty)
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    public RequestCategory(Guid id, Guid tenantId, string name, Guid? routeToUserId, Guid? urgentRouteToUserId, TaskPriority defaultPriority) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        RouteToUserId = routeToUserId;
        UrgentRouteToUserId = urgentRouteToUserId;
        DefaultPriority = defaultPriority;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public Guid? RouteToUserId { get; private set; }
    public Guid? UrgentRouteToUserId { get; private set; }
    public TaskPriority DefaultPriority { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, Guid? routeToUserId, Guid? urgentRouteToUserId, TaskPriority defaultPriority, bool isActive)
    {
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        RouteToUserId = routeToUserId;
        UrgentRouteToUserId = urgentRouteToUserId;
        DefaultPriority = defaultPriority;
        IsActive = isActive;
    }

    /// <summary>The person a request of this category and priority goes to; null when nobody is set (it waits in the inbox).</summary>
    public Guid? RouteFor(TaskPriority priority) =>
        priority == TaskPriority.High && UrgentRouteToUserId is { } urgent ? urgent : RouteToUserId;
}

/// <summary>Someone asks for something; a handler turns it into a task (the task keeps the request as its source) or rejects it.</summary>
public sealed class TaskRequest : AuditableEntity<Guid>
{
    private TaskRequest() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public TaskRequest(Guid id, Guid tenantId, Guid? categoryId, string title, string? description, TaskPriority priority, Guid requesterUserId, Guid? routedToUserId, DateOnly? neededBy) : base(id)
    {
        TenantId = tenantId;
        CategoryId = categoryId;
        Title = title.Trim();
        Description = description;
        Priority = priority;
        RequesterUserId = requesterUserId;
        RoutedToUserId = routedToUserId;
        NeededBy = neededBy;
        Status = TaskRequestStatus.New;
    }

    public Guid TenantId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public TaskPriority Priority { get; private set; }
    public DateOnly? NeededBy { get; private set; }
    public Guid RequesterUserId { get; private set; }
    public Guid? RoutedToUserId { get; private set; }
    public TaskRequestStatus Status { get; private set; }
    public string? DecisionNote { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public Guid? ConvertedTaskId { get; private set; }

    public void Convert(Guid taskId, Guid? byUserId, DateTimeOffset nowUtc)
    {
        Status = TaskRequestStatus.Converted;
        ConvertedTaskId = taskId;
        DecidedByUserId = byUserId;
        DecidedAtUtc = nowUtc;
    }

    public void Reject(string? note, Guid? byUserId, DateTimeOffset nowUtc)
    {
        Status = TaskRequestStatus.Rejected;
        DecisionNote = note;
        DecidedByUserId = byUserId;
        DecidedAtUtc = nowUtc;
    }
}
