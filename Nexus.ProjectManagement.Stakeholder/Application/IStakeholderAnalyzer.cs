namespace Nexus.ProjectManagement.StakeholderManagement.Application;

public sealed record StakeholderAnalysisSuggestion(string Name, bool IsInternal, string? Expectations, string? EngagementStrategy);

/// <summary>Optional AI integration point - same pattern as Waterfall's IWbsGenerator and
/// Risk's IRiskAnalyzer. No implementation is required for Stakeholder Management to function.</summary>
public interface IStakeholderAnalyzer
{
    Task<IReadOnlyList<StakeholderAnalysisSuggestion>> AnalyzeAsync(Guid projectId, string projectContext, CancellationToken cancellationToken);
}
