namespace Events.Application.DTOs;

public record EventDto(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string Title,
    string? Description,
    DateTime StartAtUtc,
    DateTime? EndAtUtc,
    bool IsCompleted,
    int? ReminderMinutesBefore,
    bool ReminderSent,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc);

public record CreateEventRequest(
    string Title,
    string? Description,
    DateTime StartAtUtc,
    DateTime? EndAtUtc,
    int? ReminderMinutesBefore);

public record UpdateEventRequest(
    string Title,
    string? Description,
    DateTime StartAtUtc,
    DateTime? EndAtUtc,
    bool IsCompleted,
    int? ReminderMinutesBefore);
