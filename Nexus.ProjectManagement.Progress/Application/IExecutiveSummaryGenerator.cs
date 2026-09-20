namespace Nexus.ProjectManagement.Progress.Application;

/// <summary>Optional AI integration point. Progress Management must remain fully usable
/// without it - see rule: طراحی را طوری انجام بده که Progress Management بدون AI کاملاً قابل
/// استفاده باشد.</summary>
public interface IExecutiveSummaryGenerator
{
    Task<string> GenerateAsync(Guid projectId, CancellationToken cancellationToken);
}
