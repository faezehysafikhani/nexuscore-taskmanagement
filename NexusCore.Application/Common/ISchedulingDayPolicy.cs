namespace NexusCore.Application.Common;

/// <summary>Shared host scheduling rule used by modules that create dated work.</summary>
public interface ISchedulingDayPolicy
{
    SchedulingDayDecision Check(DateOnly date);
}

public sealed record SchedulingDayDecision(bool IsAllowed, string? Reason = null)
{
    public static SchedulingDayDecision Allowed { get; } = new(true);
}

public sealed class AllowAllSchedulingDays : ISchedulingDayPolicy
{
    public SchedulingDayDecision Check(DateOnly date) => SchedulingDayDecision.Allowed;
}
