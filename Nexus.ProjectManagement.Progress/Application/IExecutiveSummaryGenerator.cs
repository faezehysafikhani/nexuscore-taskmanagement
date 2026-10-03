namespace Nexus.ProjectManagement.Progress.Application;

/// <summary>Optional executive-summary integration point. Progress Management remains fully usable without it.</summary>
public interface IExecutiveSummaryGenerator
{
    Task<string> GenerateAsync(Guid projectId, CancellationToken cancellationToken);
}
