using Nexus.ProjectManagement.Documents.Application.Dtos;
using Nexus.ProjectManagement.Documents.Domain;
using NexusCore.Application.Approvals;
using NexusCore.Application.Files;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Documents.Application;

public sealed class ProjectDocumentService(
    IProjectDocumentRepository repository,
    IFileStorage fileStorage,
    IDocumentsUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester,
    ICurrentUserContext currentUser,
    IUploadPolicyReader uploadPolicy) : IProjectDocumentService
{
    /// <summary>The caller's own document; the organization comes from the signed-in user, never from a request.</summary>
    private async Task<ProjectDocument?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId ? await repository.GetByIdAsync(tenantId, id, cancellationToken) : null;

    public async Task<Result<IReadOnlyList<ProjectDocumentDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<ProjectDocumentDto>>(Error.Unauthorized());
        }

        var documents = await repository.ListByProjectAsync(tenantId, projectId, cancellationToken);
        return Result.Success<IReadOnlyList<ProjectDocumentDto>>(documents.Select(ToDto).ToList());
    }

    public async Task<Result<ProjectDocumentDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await FindAsync(id, cancellationToken);
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

        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<ProjectDocumentDto>(Error.Unauthorized());
        }

        var checkedFile = await UploadValidation.CheckAsync(uploadPolicy, tenantId, request.FileName, content, cancellationToken);
        if (checkedFile.IsFailure)
        {
            return Result.Failure<ProjectDocumentDto>(checkedFile.Error);
        }

        var stored = await fileStorage.SaveAsync(checkedFile.Value.FileName, checkedFile.Value.ContentType, content, cancellationToken);
        var document = new ProjectDocument(
            Guid.NewGuid(), tenantId, request.ProjectId, request.Description, request.DocumentType,
            stored.StorageKey, stored.FileName, stored.ContentType, stored.SizeBytes);

        await repository.AddAsync(document, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(document));
    }

    public async Task<Result<ProjectDocumentDto>> UpdateAsync(Guid id, UpdateProjectDocumentRequest request, CancellationToken cancellationToken)
    {
        var document = await FindAsync(id, cancellationToken);
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
        var document = await FindAsync(id, cancellationToken);
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
        var document = await FindAsync(id, cancellationToken);
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
        var document = await FindAsync(id, cancellationToken);
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
