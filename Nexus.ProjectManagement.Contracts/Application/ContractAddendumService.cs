using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public sealed class ContractAddendumService(
    IContractRepository repository,
    IContractsUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IContractAddendumService
{
    public const string SubjectType = "ContractAddendum";

    public async Task<Result<IReadOnlyList<ContractAddendumDto>>> ListAsync(Guid contractId, CancellationToken cancellationToken)
    {
        if (await repository.GetContractAsync(contractId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<ContractAddendumDto>>(Error.NotFound("Contract not found."));
        }

        var addenda = await repository.ListAddendaAsync(contractId, cancellationToken);
        return Result.Success<IReadOnlyList<ContractAddendumDto>>(addenda.OrderBy(a => a.Number).Select(ContractMapper.ToDto).ToList());
    }

    public async Task<Result<ContractAddendumDto>> CreateAsync(Guid contractId, CreateContractAddendumRequest request, CancellationToken cancellationToken)
    {
        var contract = await repository.GetContractAsync(contractId, cancellationToken);
        if (contract is null)
        {
            return Result.Failure<ContractAddendumDto>(Error.NotFound("Contract not found."));
        }

        var error = ContractIsOpen(contract) ?? Validate(request.Title, request.AmountChange, request.ExtensionDays);
        if (error is not null)
        {
            return Result.Failure<ContractAddendumDto>(error);
        }

        var existing = await repository.ListAddendaAsync(contractId, cancellationToken);
        var number = existing.Count == 0 ? 1 : existing.Max(a => a.Number) + 1;
        var addendum = new ContractAddendum(
            Guid.NewGuid(), request.TenantId, contractId, number, request.Title, request.Description,
            request.AddendumDate, request.AmountChange, request.ExtensionDays);

        await repository.AddAddendumAsync(addendum, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ContractMapper.ToDto(addendum));
    }

    public async Task<Result<ContractAddendumDto>> UpdateAsync(Guid contractId, Guid addendumId, UpdateContractAddendumRequest request, CancellationToken cancellationToken)
    {
        var addendum = await GetOwnedAsync(contractId, addendumId, cancellationToken);
        if (addendum is null)
        {
            return Result.Failure<ContractAddendumDto>(Error.NotFound("Addendum not found."));
        }

        if (addendum.ApprovalStatus is ApprovalStatus.PendingApproval or ApprovalStatus.Approved)
        {
            return Result.Failure<ContractAddendumDto>(Error.Conflict("An addendum that is pending approval or approved cannot be edited."));
        }

        var error = Validate(request.Title, request.AmountChange, request.ExtensionDays);
        if (error is not null)
        {
            return Result.Failure<ContractAddendumDto>(error);
        }

        addendum.UpdateDetails(request.Title, request.Description, request.AddendumDate, request.AmountChange, request.ExtensionDays);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ContractMapper.ToDto(addendum));
    }

    public async Task<Result> DeleteAsync(Guid contractId, Guid addendumId, CancellationToken cancellationToken)
    {
        var addendum = await GetOwnedAsync(contractId, addendumId, cancellationToken);
        if (addendum is null)
        {
            return Result.Failure(Error.NotFound("Addendum not found."));
        }

        if (addendum.ApprovalStatus is ApprovalStatus.PendingApproval or ApprovalStatus.Approved)
        {
            return Result.Failure(Error.Conflict("An addendum that is pending approval or approved cannot be deleted."));
        }

        await repository.RemoveAddendaAsync([addendum], cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ContractAddendumDto>> SubmitForApprovalAsync(Guid contractId, Guid addendumId, CancellationToken cancellationToken)
    {
        var addendum = await GetOwnedAsync(contractId, addendumId, cancellationToken);
        if (addendum is null)
        {
            return Result.Failure<ContractAddendumDto>(Error.NotFound("Addendum not found."));
        }

        if (addendum.ApprovalStatus is ApprovalStatus.PendingApproval or ApprovalStatus.Approved)
        {
            return Result.Failure<ContractAddendumDto>(Error.Conflict("The addendum has already been submitted for approval."));
        }

        var contract = (await repository.GetContractAsync(contractId, cancellationToken))!;
        var open = ContractIsOpen(contract);
        if (open is not null)
        {
            return Result.Failure<ContractAddendumDto>(open);
        }

        // A reduction must not take the contract below what has already been billed against it.
        var addenda = await repository.ListAddendaAsync(contractId, cancellationToken);
        var invoices = await repository.ListInvoicesAsync(contractId, cancellationToken);
        var summary = ContractCalculator.Summarize(contract, addenda, invoices);
        if (summary.CurrentAmount + addendum.AmountChange < summary.ReservedAmount)
        {
            return Result.Failure<ContractAddendumDto>(Error.Conflict("The addendum would reduce the contract below the amount already invoiced."));
        }

        var subject = new ApprovalSubject(SubjectType, addendum.Id, addendum.TenantId, ScopeType: "Project", ScopeId: contract.ProjectId);
        if (await approvalRequester.RequestApprovalAsync(subject, cancellationToken) == ApprovalRequestOutcome.Submitted)
        {
            addendum.MarkPendingApproval();
        }
        else
        {
            addendum.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ContractMapper.ToDto(addendum));
    }

    private async Task<ContractAddendum?> GetOwnedAsync(Guid contractId, Guid addendumId, CancellationToken cancellationToken)
    {
        var addendum = await repository.GetAddendumAsync(addendumId, cancellationToken);
        return addendum is not null && addendum.ContractId == contractId ? addendum : null;
    }

    /// <summary>Addenda change a signed, approved contract that is still running or finished - not a draft or a terminated one.</summary>
    private static Error? ContractIsOpen(Contract contract)
    {
        if (contract.ApprovalStatus != ApprovalStatus.Approved)
        {
            return Error.Conflict("Addenda can only be added to an approved contract.");
        }

        return contract.Status == ContractStatus.Terminated
            ? Error.Conflict("A terminated contract cannot be amended.")
            : null;
    }

    private static Error? Validate(string title, decimal amountChange, int extensionDays)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("Title is required.");
        }

        if (amountChange == 0 && extensionDays == 0)
        {
            return Error.Validation("An addendum must change the amount, the duration, or both.");
        }

        if (Math.Abs(amountChange) > ContractMapper.MaxAmount)
        {
            return Error.Validation($"The amount change must be within {ContractMapper.MaxAmount:N0} rials.");
        }

        return Math.Abs(extensionDays) > 36500
            ? Error.Validation("The extension must be within 36,500 days.")
            : null;
    }
}
