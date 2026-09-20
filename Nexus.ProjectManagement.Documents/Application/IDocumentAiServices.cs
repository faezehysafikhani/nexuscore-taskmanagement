namespace Nexus.ProjectManagement.Documents.Application;

/// <summary>Optional AI integration points (Document Summary, Document Relevance) - same
/// pattern as every other capability's AI contract. Neither is required for Project Documents
/// to function.</summary>
public interface IDocumentSummaryGenerator
{
    Task<string> SummarizeAsync(Guid documentId, CancellationToken cancellationToken);
}

public interface IDocumentRelevanceAnalyzer
{
    Task<string> AnalyzeRelevanceAsync(Guid documentId, Guid projectId, CancellationToken cancellationToken);
}
