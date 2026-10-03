using Nexus.ProjectManagement.Documents.Application.Dtos;
using Nexus.ProjectManagement.Documents.Domain;
using NexusCore.Application.Approvals;
using NexusCore.Application.Files;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Documents.Application;

public sealed class ProjectDocumentService(
    IProjectDocumentRepository repository,
    IFileStorage fileStorage,
    IDocumentsUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IProjectDocumentService
{
    public async Task<Result<IReadOnlyList<ProjectDocumentDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var documents = await repository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<ProjectDocumentDto>>(documents.Select(ToDto).ToList());
    }

    public async Task<Result<ProjectDocumentDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        return document is null
            ? Result.Failure<ProjectDocumentDto>(Error.NotFound("Document not found."))
            : Result.Success(ToDto(document));
    }

    public async Task<Result<ProjectDocumentDto>> UploadAsync(UploadProjectDocumentRequest request, Stream content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<ProjectDocumentDto>(Error.Validation("Description is required."));
        }

        var stored = await fileStorage.SaveAsync(request.FileName, request.ContentType, content, cancellationToken);
        var document = new ProjectDocument(
            Guid.NewGuid(), request.TenantId, request.ProjectId, request.Description, request.DocumentType,
            stored.StorageKey, stored.FileName, stored.ContentType, stored.SizeBytes);

        await repository.AddAsync(document, cancellationToken);
        await repository.AddVersionAsync(NewVersion(document, document.CurrentVersion, comment: null), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(document));
    }

    public async Task<Result<ProjectDocumentDto>> UpdateAsync(Guid id, UpdateProjectDocumentRequest request, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return Result.Failure<ProjectDocumentDto>(Error.NotFound("Document not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<ProjectDocumentDto>(Error.Validation("Description is required."));
        }

        document.UpdateDescription(request.Description, request.DocumentType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(document));
    }

    public async Task<Result<(Stream Content, string FileName, string ContentType)>> DownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return Result.Failure<(Stream, string, string)>(Error.NotFound("Document not found."));
        }

        var content = await fileStorage.OpenReadAsync(document.StorageKey, cancellationToken);
        if (content is null)
        {
            return Result.Failure<(Stream, string, string)>(Error.NotFound("The stored file is missing."));
        }

        return Result.Success<(Stream, string, string)>((content, document.FileName, document.ContentType));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return Result.Failure(Error.NotFound("Document not found."));
        }

        // Every version's file goes with the document, not just the current one. The current
        // file is also the newest version's, so the keys are de-duplicated.
        var versions = await repository.ListVersionsAsync(id, cancellationToken);
        foreach (var storageKey in versions.Select(version => version.StorageKey).Append(document.StorageKey).Distinct())
        {
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
        }

        await repository.RemoveVersionsAsync(versions, cancellationToken);
        await repository.RemoveAsync(document, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ProjectDocumentVersionDto>>> ListVersionsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<IReadOnlyList<ProjectDocumentVersionDto>>(Error.NotFound("Document not found."));
        }

        var versions = await repository.ListVersionsAsync(documentId, cancellationToken);
        var dtos = versions.Count > 0
            ? versions.Select(version => ToDto(version, document.CurrentVersion))
            // A document that predates version history: its one file is version 1.
            : [ToImplicitFirstVersionDto(document)];

        return Result.Success<IReadOnlyList<ProjectDocumentVersionDto>>(dtos.OrderByDescending(version => version.VersionNumber).ToList());
    }

    public async Task<Result<ProjectDocumentVersionDto>> UploadVersionAsync(
        Guid documentId, UploadProjectDocumentVersionRequest request, Stream content, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<ProjectDocumentVersionDto>(Error.NotFound("Document not found."));
        }

        // Approvers are reviewing the current file; swapping it underneath them would let an
        // unreviewed file inherit the approval.
        if (document.ApprovalStatus == ApprovalStatus.PendingApproval)
        {
            return Result.Failure<ProjectDocumentVersionDto>(Error.Conflict("A document that is pending approval cannot receive a new version."));
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return Result.Failure<ProjectDocumentVersionDto>(Error.Validation("File name is required."));
        }

        var stored = await fileStorage.SaveAsync(request.FileName, request.ContentType, content, cancellationToken);
        try
        {
            // A document that predates version history has no rows yet; record its current file
            // as version 1 first, so the original stays in the history instead of being orphaned.
            var existing = await repository.ListVersionsAsync(documentId, cancellationToken);
            if (existing.Count == 0)
            {
                await repository.AddVersionAsync(NewVersion(document, document.CurrentVersion, comment: null), cancellationToken);
            }

            document.ReplaceFile(stored.StorageKey, stored.FileName, stored.ContentType, stored.SizeBytes);
            var version = NewVersion(document, document.CurrentVersion, request.Comment);
            await repository.AddVersionAsync(version, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(version, document.CurrentVersion));
        }
        catch
        {
            // Nothing references the new file if the save failed; do not leave it behind.
            await fileStorage.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }
    }

    public async Task<Result<(Stream Content, string FileName, string ContentType)>> DownloadVersionAsync(
        Guid documentId, int versionNumber, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<(Stream, string, string)>(Error.NotFound("Document not found."));
        }

        var versions = await repository.ListVersionsAsync(documentId, cancellationToken);
        var version = versions.SingleOrDefault(candidate => candidate.VersionNumber == versionNumber);

        string storageKey, fileName, contentType;
        if (version is not null)
        {
            (storageKey, fileName, contentType) = (version.StorageKey, version.FileName, version.ContentType);
        }
        else if (versions.Count == 0 && versionNumber == document.CurrentVersion)
        {
            (storageKey, fileName, contentType) = (document.StorageKey, document.FileName, document.ContentType);
        }
        else
        {
            return Result.Failure<(Stream, string, string)>(Error.NotFound("Document version not found."));
        }

        var stream = await fileStorage.OpenReadAsync(storageKey, cancellationToken);
        return stream is null
            ? Result.Failure<(Stream, string, string)>(Error.NotFound("The stored file is missing."))
            : Result.Success<(Stream, string, string)>((stream, fileName, contentType));
    }

    public async Task<Result<ProjectDocumentDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return Result.Failure<ProjectDocumentDto>(Error.NotFound("Document not found."));
        }

        var subject = new ApprovalSubject("ProjectDocument", document.Id, document.TenantId, ScopeType: "Project", ScopeId: document.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            document.MarkPendingApproval();
        }
        else
        {
            document.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(document));
    }

    private static ProjectDocumentVersion NewVersion(ProjectDocument document, int versionNumber, string? comment) => new(
        Guid.NewGuid(), document.TenantId, document.Id, versionNumber,
        document.StorageKey, document.FileName, document.ContentType, document.SizeBytes, comment);

    private static ProjectDocumentVersionDto ToDto(ProjectDocumentVersion version, int currentVersion) => new(
        version.Id, version.DocumentId, version.VersionNumber, version.FileName, version.ContentType, version.SizeBytes,
        version.Comment, version.VersionNumber == currentVersion, version.CreatedByUserId, version.CreatedAtUtc);

    private static ProjectDocumentVersionDto ToImplicitFirstVersionDto(ProjectDocument document) => new(
        Id: null, document.Id, document.CurrentVersion, document.FileName, document.ContentType, document.SizeBytes,
        Comment: null, IsCurrent: true, document.CreatedByUserId, document.CreatedAtUtc);

    private static ProjectDocumentDto ToDto(ProjectDocument document) => new(
        document.Id, document.TenantId, document.ProjectId, document.Description, document.DocumentType,
        document.RegisterDate, document.FileName, document.ContentType, document.SizeBytes,
        document.ApprovalStatus, document.CreatedByUserId, document.CurrentVersion);
}
