namespace Nexus.ProjectManagement.Agile.Application;

public sealed record GeneratedAgileTaskSuggestion(string Title, string? Description, int Priority);

/// <summary>Optional task-generation integration point - same pattern as Waterfall's
/// IWbsGenerator. No implementation is required for Agile Planning to function.</summary>
public interface IAgileTaskGenerator
{
    Task<IReadOnlyList<GeneratedAgileTaskSuggestion>> GenerateAsync(Guid projectId, string projectGoal, CancellationToken cancellationToken);
}
