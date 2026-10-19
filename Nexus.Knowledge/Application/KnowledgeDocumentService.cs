using Nexus.Knowledge.Application.Dtos;
using Nexus.Knowledge.Domain;
using NexusCore.Application.Files;
using NexusCore.SharedKernel.Results;

namespace Nexus.Knowledge.Application;

public sealed class KnowledgeDocumentService(
    IKnowledgeDocumentRepository repository,
    IFileStorage fileStorage,
    IKnowledgeUnitOfWork unitOfWork) : IKnowledgeDocumentService
{
    public Task<Result<IReadOnlyList<KnowledgeDocumentDto>>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, CancellationToken cancellationToken) =>
                    SearchAsync(tenantId, search, documentType, null, null, cancellationToken);

    public async Task<Result<IReadOnlyList<KnowledgeDocumentDto>>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, string? subjectType, Guid? subjectId, CancellationToken cancellationToken)
    {
        var hasSubjectType = !string.IsNullOrWhiteSpace(subjectType);

        if (hasSubjectType != subjectId.HasValue)
        {
            return Result.Failure<IReadOnlyList<KnowledgeDocumentDto>>(
                Error.Validation("SubjectType and SubjectId must be supplied together."));
        }

        if (hasSubjectType && (subjectType!.Trim().Length > 100 || subjectId == Guid.Empty))
        {
            return Result.Failure<IReadOnlyList<KnowledgeDocumentDto>>(
                Error.Validation("The subject filter is invalid."));
        }

        var documents = await repository.SearchAsync(tenantId, search, documentType, hasSubjectType ? subjectType!.Trim() : null, subjectId, cancellationToken);

        return Result.Success<IReadOnlyList<KnowledgeDocumentDto>>(
            documents.Select(ToDto).ToList());
    }

    public async Task<Result<KnowledgeDocumentDto>> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdForTenantAsync(tenantId, id, cancellationToken);
        return document is null
            ? Result.Failure<KnowledgeDocumentDto>(Error.NotFound("Knowledge document not found."))
            : Result.Success(ToDto(document));
    }

    public async Task<Result<KnowledgeDocumentDto>> UploadAsync(UploadKnowledgeDocumentRequest request, Stream content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<KnowledgeDocumentDto>(Error.Validation("Title is required."));
        }

        var stored = await fileStorage.SaveAsync(request.FileName, request.ContentType, content, cancellationToken);
        var document = new KnowledgeDocument(
            Guid.NewGuid(), request.TenantId, request.Title, request.DocumentType,
            stored.StorageKey, stored.FileName, stored.ContentType, stored.SizeBytes);
        document.UpdateDetails(request.Title, request.Description, request.DocumentType);

        await repository.AddAsync(document, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(document));
    }

    public async Task<Result<KnowledgeDocumentDto>> UpdateAsync(Guid tenantId, Guid id, UpdateKnowledgeDocumentRequest request, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdForTenantAsync(tenantId, id, cancellationToken);
        if (document is null)
        {
            return Result.Failure<KnowledgeDocumentDto>(Error.NotFound("Knowledge document not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<KnowledgeDocumentDto>(Error.Validation("Title is required."));
        }

        document.UpdateDetails(request.Title, request.Description, request.DocumentType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(document));
    }

    public async Task<Result<KnowledgeDocumentLinkDto>> AddLinkAsync(Guid tenantId, Guid documentId, CreateKnowledgeDocumentLinkRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectType) || request.SubjectType.Trim().Length > 100 || request.SubjectId == Guid.Empty)
        {
            return Result.Failure<KnowledgeDocumentLinkDto>(
                Error.Validation("The document link is invalid."));
        }

        var document = await repository.GetByIdForTenantAsync(tenantId, documentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure<KnowledgeDocumentLinkDto>(
                Error.NotFound("Knowledge document not found."));
        }

        var subjectType = request.SubjectType.Trim();

        var existing = await repository.GetLinkAsync(documentId, subjectType, request.SubjectId, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<KnowledgeDocumentLinkDto>(
                Error.Conflict("The document is already linked to this subject."));
        }

        var link = new KnowledgeDocumentLink(Guid.NewGuid(), documentId, subjectType, request.SubjectId);

        await repository.AddLinkAsync(link, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new KnowledgeDocumentLinkDto(link.Id, link.DocumentId, link.SubjectType, link.SubjectId));
    }

    public async Task<Result> RemoveLinkAsync(Guid tenantId, Guid documentId, string subjectType, Guid subjectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subjectType) || subjectType.Trim().Length > 100 || subjectId == Guid.Empty)
        {
            return Result.Failure(
                Error.Validation("The document link is invalid."));
        }

        var document = await repository.GetByIdForTenantAsync(tenantId, documentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure(
                Error.NotFound("Knowledge document not found."));
        }

        var link = await repository.GetLinkAsync(documentId, subjectType.Trim(), subjectId, cancellationToken);

        if (link is null)
        {
            return Result.Failure(
                Error.NotFound("Knowledge document link not found."));
        }

        await repository.RemoveLinkAsync(link, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<(Stream Content, string FileName, string ContentType)>> DownloadAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdForTenantAsync(tenantId, id, cancellationToken);
        if (document is null)
        {
            return Result.Failure<(Stream, string, string)>(Error.NotFound("Knowledge document not found."));
        }

        var content = await fileStorage.OpenReadAsync(document.StorageKey, cancellationToken);
        if (content is null)
        {
            return Result.Failure<(Stream, string, string)>(Error.NotFound("The stored file is missing."));
        }

        return Result.Success<(Stream, string, string)>((content, document.FileName, document.ContentType));
    }

    public async Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdForTenantAsync(tenantId, id, cancellationToken);
        if (document is null)
        {
            return Result.Failure(Error.NotFound("Knowledge document not found."));
        }

        await fileStorage.DeleteAsync(document.StorageKey, cancellationToken);
        await repository.RemoveAsync(document, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static KnowledgeDocumentDto ToDto(KnowledgeDocument document) => new(
        document.Id, document.TenantId, document.Title, document.Description, document.DocumentType,
        document.FileName, document.ContentType, document.SizeBytes, document.CreatedAtUtc);
}
