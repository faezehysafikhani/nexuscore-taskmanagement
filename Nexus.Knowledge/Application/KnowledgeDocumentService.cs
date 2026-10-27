using Nexus.Knowledge.Application.Dtos;
using Nexus.Knowledge.Domain;
using NexusCore.Application.Files;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Knowledge.Application;

public sealed class KnowledgeDocumentService(
    IKnowledgeDocumentRepository repository,
    IFileStorage fileStorage,
    IKnowledgeUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IUploadPolicyReader uploadPolicy) : IKnowledgeDocumentService
{
    private static readonly Error NotFound = Error.NotFound("Knowledge document not found.");

    /// <summary>The caller's own document; the organization comes from the signed-in user, never from a request.</summary>
    private async Task<KnowledgeDocument?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId ? await repository.GetByIdAsync(tenantId, id, cancellationToken) : null;

    public async Task<Result<IReadOnlyList<KnowledgeDocumentDto>>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId != tenantId)
        {
            return Result.Failure<IReadOnlyList<KnowledgeDocumentDto>>(Error.Unauthorized());
        }

        var documents = await repository.SearchAsync(tenantId, search, documentType, cancellationToken);
        return Result.Success<IReadOnlyList<KnowledgeDocumentDto>>(documents.Select(ToDto).ToList());
    }

    public async Task<Result<KnowledgeDocumentDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await FindAsync(id, cancellationToken);
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

        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<KnowledgeDocumentDto>(Error.Unauthorized());
        }

        var checkedFile = await UploadValidation.CheckAsync(uploadPolicy, tenantId, request.FileName, content, cancellationToken);
        if (checkedFile.IsFailure)
        {
            return Result.Failure<KnowledgeDocumentDto>(checkedFile.Error);
        }

        var stored = await fileStorage.SaveAsync(checkedFile.Value.FileName, checkedFile.Value.ContentType, content, cancellationToken);
        var document = new KnowledgeDocument(
            Guid.NewGuid(), tenantId, request.Title, request.DocumentType,
            stored.StorageKey, stored.FileName, stored.ContentType, stored.SizeBytes);
        document.UpdateDetails(request.Title, request.Description, request.DocumentType);

        await repository.AddAsync(document, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(document));
    }

    public async Task<Result<KnowledgeDocumentDto>> UpdateAsync(Guid id, UpdateKnowledgeDocumentRequest request, CancellationToken cancellationToken)
    {
        var document = await FindAsync(id, cancellationToken);
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
        var document = await FindAsync(id, cancellationToken);
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
        var document = await FindAsync(id, cancellationToken);
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
