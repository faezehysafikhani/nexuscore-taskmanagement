using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public sealed record ContractLinkDto(
    Guid Id, Guid ContractId, ContractLinkSubject SubjectType, Guid SubjectId, ContractLinkKind Kind, Guid TargetId,
    string Title, string? Note, Guid? CreatedByUserId);

/// <summary>SubjectId is left out for a link on the contract itself. Title is the name of the target, which this module cannot look up.</summary>
public sealed record CreateContractLinkRequest(
    ContractLinkKind Kind, Guid TargetId, string Title, ContractLinkSubject SubjectType = ContractLinkSubject.Contract,
    Guid? SubjectId = null, string? Note = null);

public interface IContractLinkService
{
    Task<Result<IReadOnlyList<ContractLinkDto>>> ListAsync(Guid contractId, CancellationToken cancellationToken);
    Task<Result<ContractLinkDto>> CreateAsync(Guid contractId, CreateContractLinkRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid contractId, Guid id, CancellationToken cancellationToken);
}

public sealed class ContractLinkService(
    IContractRepository contracts,
    ICommitmentRepository commitments,
    IContractsUnitOfWork unitOfWork) : IContractLinkService
{
    public async Task<Result<IReadOnlyList<ContractLinkDto>>> ListAsync(Guid contractId, CancellationToken cancellationToken)
    {
        if (await contracts.GetContractAsync(contractId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<ContractLinkDto>>(Error.NotFound("Contract not found."));
        }

        var links = await commitments.ListLinksAsync(contractId, cancellationToken);
        return Result.Success<IReadOnlyList<ContractLinkDto>>(links.OrderBy(l => l.CreatedAtUtc).Select(ToDto).ToList());
    }

    public async Task<Result<ContractLinkDto>> CreateAsync(Guid contractId, CreateContractLinkRequest request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetContractAsync(contractId, cancellationToken);
        if (contract is null)
        {
            return Result.Failure<ContractLinkDto>(Error.NotFound("Contract not found."));
        }

        if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.SubjectType))
        {
            return Result.Failure<ContractLinkDto>(Error.Validation("Unknown kind or subject."));
        }

        if (request.TargetId == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 300)
        {
            return Result.Failure<ContractLinkDto>(Error.Validation("The target and its title (at most 300 characters) are required."));
        }

        if (request.Note is { Length: > 1000 })
        {
            return Result.Failure<ContractLinkDto>(Error.Validation("The note is at most 1000 characters."));
        }

        // The subject must be this contract, or a milestone / obligation of it.
        Guid subjectId;
        switch (request.SubjectType)
        {
            case ContractLinkSubject.Contract:
                subjectId = contractId;
                break;
            case ContractLinkSubject.Milestone:
                subjectId = request.SubjectId ?? Guid.Empty;
                if ((await commitments.GetMilestoneAsync(subjectId, cancellationToken))?.ContractId != contractId)
                {
                    return Result.Failure<ContractLinkDto>(Error.NotFound("Milestone not found."));
                }

                break;
            default:
                subjectId = request.SubjectId ?? Guid.Empty;
                if ((await commitments.GetObligationAsync(subjectId, cancellationToken))?.ContractId != contractId)
                {
                    return Result.Failure<ContractLinkDto>(Error.NotFound("Obligation not found."));
                }

                break;
        }

        if ((await commitments.ListLinksAsync(contractId, cancellationToken))
            .Any(l => l.SubjectType == request.SubjectType && l.SubjectId == subjectId && l.Kind == request.Kind && l.TargetId == request.TargetId))
        {
            return Result.Failure<ContractLinkDto>(Error.Conflict("This is already linked here."));
        }

        var link = new ContractLink(Guid.NewGuid(), contract.TenantId, contractId, request.SubjectType, subjectId, request.Kind, request.TargetId, request.Title, request.Note);
        await commitments.AddLinkAsync(link, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(link));
    }

    public async Task<Result> DeleteAsync(Guid contractId, Guid id, CancellationToken cancellationToken)
    {
        var link = await commitments.GetLinkAsync(id, cancellationToken);
        if (link is null || link.ContractId != contractId)
        {
            return Result.Failure(Error.NotFound("Link not found."));
        }

        await commitments.RemoveLinkAsync(link, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static ContractLinkDto ToDto(ContractLink l) =>
        new(l.Id, l.ContractId, l.SubjectType, l.SubjectId, l.Kind, l.TargetId, l.Title, l.Note, l.CreatedByUserId);
}
