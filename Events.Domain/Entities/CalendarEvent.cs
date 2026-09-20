using NexusCore.SharedKernel.Domain;

namespace Events.Domain.Entities;

public class CalendarEvent : AuditableEntity<Guid>
{
    private CalendarEvent() : base(Guid.Empty) { }

    public CalendarEvent(
        Guid id,
        Guid tenantId,
        Guid userId,
        string title,
        string? description,
        DateTime startAtUtc,
        DateTime? endAtUtc,
        int? reminderMinutesBefore)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Title = title;
        Description = description;
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        IsCompleted = false;
        ReminderMinutesBefore = reminderMinutesBefore;
        ReminderSent = false;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public DateTime StartAtUtc { get; private set; }
    public DateTime? EndAtUtc { get; private set; }
    public bool IsCompleted { get; private set; }
    public int? ReminderMinutesBefore { get; private set; }
    public bool ReminderSent { get; private set; }

    public void Update(
        string title,
        string? description,
        DateTime startAtUtc,
        DateTime? endAtUtc,
        bool isCompleted,
        int? reminderMinutesBefore)
    {
        Title = title;
        Description = description;
        if (StartAtUtc != startAtUtc || ReminderMinutesBefore != reminderMinutesBefore)
        {
            ReminderSent = false;
        }
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        IsCompleted = isCompleted;
        ReminderMinutesBefore = reminderMinutesBefore;
    }

    public void SetCompleted(bool isCompleted)
    {
        IsCompleted = isCompleted;
    }

    public void MarkReminderSent()
    {
        ReminderSent = true;
    }
}
