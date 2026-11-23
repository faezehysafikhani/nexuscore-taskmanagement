namespace Nexus.TaskManagement.Application;

/// <summary>
/// Which days count when an SLA runs on working days only (CAT-85): the organization's work calendar, when the host
/// installs one. Optional: without it (or with no default calendar) the host's scheduling rule decides, as before.
/// </summary>
public interface ISlaWorkingDays
{
    /// <summary>A test for the days from <paramref name="from"/> to <paramref name="to"/>; null when there is no calendar to follow.</summary>
    Task<Func<DateOnly, bool>?> GetAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
