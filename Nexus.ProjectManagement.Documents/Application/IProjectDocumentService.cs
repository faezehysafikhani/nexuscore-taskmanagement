using Nexus.ProjectManagement.Documents.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Documents.Application;

public interface IProjectDocumentService
{
    Task<Result<IReadOnlyList<ProjectDocumentDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<ProjectDocumentDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ProjectDocumentDto>> UploadAsync(UploadProjectDocumentRequest request, Stream content, CancellationToken cancellationToken);
    Task<Result<ProjectDocumentDto>> UpdateAsync(Guid id, UpdateProjectDocumentRequest request, CancellationToken cancellationToken);
    Task<Result<(Stream Content, string FileName, string ContentType)>> DownloadAsync(Guid id, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every file the document has had, newest first; the current one is flagged.</summary>
    Task<Result<IReadOnlyList<ProjectDocumentVersionDto>>> ListVersionsAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>Uploads a new file as the document's next version. The earlier files are kept.</summary>
    Task<Result<ProjectDocumentVersionDto>> UploadVersionAsync(Guid documentId, UploadProjectDocumentVersionRequest request, Stream content, CancellationToken cancellationToken);

    Task<Result<(Stream Content, string FileName, string ContentType)>> DownloadVersionAsync(Guid documentId, int versionNumber, CancellationToken cancellationToken);
    Task<Result<ProjectDocumentDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
