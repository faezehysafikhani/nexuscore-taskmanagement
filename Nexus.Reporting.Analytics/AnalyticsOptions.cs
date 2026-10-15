namespace Nexus.Reporting.Analytics;

/// <summary>Opt-in behaviour of the analytics reports; every switch defaults to how they behaved before it existed.</summary>
public sealed class AnalyticsOptions
{
    /// <summary>
    /// When true, a project's planned and actual progress come from what its planning modules work out (the schedule, the board - the
    /// same figures the project screens show) instead of from the progress updates people register. Needs the Core progress service.
    /// </summary>
    public bool ProgressFromProjectPlan { get; set; }

    /// <summary>When true, projects that are not approved (drafts, waiting, rejected) are left out of the unit and manager reports.</summary>
    public bool OnlyApprovedProjects { get; set; }
}
