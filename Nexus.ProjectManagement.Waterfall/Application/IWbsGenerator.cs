namespace Nexus.ProjectManagement.Waterfall.Application;

public sealed record GeneratedActivitySuggestion(string Name, string? Description, int? DurationDays, decimal Weight);

/// <summary>
/// Optional work-breakdown integration point. Nothing in this module requires an implementation to be
/// registered - the endpoint resolves this as IWbsGenerator? and reports 501 when absent, so
/// Waterfall Planning is fully usable without a provider installed.
/// </summary>
public interface IWbsGenerator
{
    Task<IReadOnlyList<GeneratedActivitySuggestion>> GenerateAsync(Guid projectId, string projectGoal, CancellationToken cancellationToken);
}
