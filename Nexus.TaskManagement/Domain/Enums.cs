namespace Nexus.TaskManagement.Domain;

/// <summary>Mirrors the UI's TaskStatus union ('todo' | 'in_progress' | 'paused' | 'completed').</summary>
public enum TaskItemStatus
{
    Todo = 0,
    InProgress = 1,
    Paused = 2,
    Completed = 3
}

/// <summary>Mirrors the UI's Priority union ('low' | 'medium' | 'high').</summary>
public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// Mirrors the UI's SubTaskImportance union. Shown to the user as "وزن ۱/۲/۳", so the
/// numeric order matters and must not be reshuffled.
/// </summary>
public enum SubTaskImportance
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>Mirrors the UI's RecurringFrequency union.</summary>
public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    MonthlyDay = 3,
    MonthlyNthWeekday = 4
}

/// <summary>Mirrors the UI's OccurrenceNth union, used only by MonthlyNthWeekday.</summary>
public enum OccurrenceNth
{
    First = 0,
    Second = 1,
    Third = 2,
    Fourth = 3,
    Last = 4
}
