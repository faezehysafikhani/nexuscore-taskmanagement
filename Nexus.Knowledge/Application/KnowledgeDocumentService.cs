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
    public async Task<Result<IReadOnlyList<KnowledgeDocumentDto>>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, CancellationToken cancellationToken)
    {
        var documents = await repository.SearchAsync(tenantId, search, documentType, cancellationToken);
        return Result.Success<IReadOnlyList<KnowledgeDocumentDto>>(documents.Select(ToDto).ToList());
    }

    public async Task<Result<KnowledgeDocumentDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
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

    public async Task<Result<KnowledgeDocumentDto>> UpdateAsync(Guid id, UpdateKnowledgeDocumentRequest request, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
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

    public async Task<Result<(Stream Content, string FileName, string ContentType)>> DownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
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

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
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
