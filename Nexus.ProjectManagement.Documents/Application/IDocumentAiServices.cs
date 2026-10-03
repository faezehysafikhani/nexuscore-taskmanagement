namespace Nexus.ProjectManagement.Documents.Application;

/// <summary>Optional integration points for document summary and relevance. Neither is required for Project Documents
/// to function.</summary>
public interface IDocumentSummaryGenerator
{
    Task<string> SummarizeAsync(Guid documentId, CancellationToken cancellationToken);
}

public interface IDocumentRelevanceAnalyzer
{
    Task<string> AnalyzeRelevanceAsync(Guid documentId, Guid projectId, CancellationToken cancellationToken);
}
