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

        await fileStorage.DeleteAsync(document.StorageKey, cancellationToken);
        await repository.RemoveAsync(document, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
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

    private static ProjectDocumentDto ToDto(ProjectDocument document) => new(
        document.Id, document.TenantId, document.ProjectId, document.Description, document.DocumentType,
        document.RegisterDate, document.FileName, document.ContentType, document.SizeBytes,
        document.ApprovalStatus, document.CreatedByUserId);
}
