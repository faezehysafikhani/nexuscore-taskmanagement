namespace Nexus.ProjectManagement.RiskManagement.Application;

public sealed record RiskAnalysisSuggestion(string Description, int ProbabilityScore, int SeverityScore, int ImpactScore, string? SuggestedResponsePlan);

/// <summary>
/// Optional AI integration point - see Nexus.ProjectManagement.Waterfall's IWbsGenerator for
/// the same pattern. No implementation is required for Risk Management to function.
/// </summary>
public interface IRiskAnalyzer
{
    Task<IReadOnlyList<RiskAnalysisSuggestion>> AnalyzeAsync(Guid projectId, string projectContext, CancellationToken cancellationToken);
}
