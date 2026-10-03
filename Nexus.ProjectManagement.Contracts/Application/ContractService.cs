using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public sealed class ContractService(
    IContractRepository repository,
    IContractsUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IContractService
{
    public const int MaxNumberLength = 50;
    public const string SubjectType = "Contract";

    public async Task<Result<IReadOnlyList<ContractDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var contracts = await repository.ListContractsAsync(projectId, cancellationToken);
        var ids = contracts.Select(c => c.Id).ToList();
        var addenda = (await repository.ListAddendaAsync(ids, cancellationToken)).GroupBy(a => a.ContractId).ToDictionary(g => g.Key, g => g.ToList());
        var invoices = (await repository.ListInvoicesAsync(ids, cancellationToken)).GroupBy(i => i.ContractId).ToDictionary(g => g.Key, g => g.ToList());

        return Result.Success<IReadOnlyList<ContractDto>>(contracts
            .OrderBy(c => c.ContractNumber, StringComparer.Ordinal)
            .Select(c => ContractMapper.ToDto(c, ContractCalculator.Summarize(c, addenda.GetValueOrDefault(c.Id) ?? [], invoices.GetValueOrDefault(c.Id) ?? [])))
            .ToList());
    }

    public async Task<Result<ContractDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var contract = await repository.GetContractAsync(id, cancellationToken);
        if (contract is null)
        {
            return Result.Failure<ContractDetailDto>(Error.NotFound("Contract not found."));
        }

        var addenda = await repository.ListAddendaAsync(id, cancellationToken);
        var invoices = await repository.ListInvoicesAsync(id, cancellationToken);
        return Result.Success(new ContractDetailDto(
            ContractMapper.ToDto(contract, ContractCalculator.Summarize(contract, addenda, invoices)),
            addenda.OrderBy(a => a.Number).Select(ContractMapper.ToDto).ToList(),
            invoices.OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNumber, StringComparer.Ordinal).Select(ContractMapper.ToDto).ToList()));
    }

    public async Task<Result<ProjectContractsSummaryDto>> GetProjectSummaryAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var contracts = await repository.ListContractsAsync(projectId, cancellationToken);
        var ids = contracts.Select(c => c.Id).ToList();
        var addenda = (await repository.ListAddendaAsync(ids, cancellationToken)).GroupBy(a => a.ContractId).ToDictionary(g => g.Key, g => g.ToList());
        var invoices = (await repository.ListInvoicesAsync(ids, cancellationToken)).GroupBy(i => i.ContractId).ToDictionary(g => g.Key, g => g.ToList());

        var summaries = contracts
            .Select(c => ContractCalculator.Summarize(c, addenda.GetValueOrDefault(c.Id) ?? [], invoices.GetValueOrDefault(c.Id) ?? []))
            .ToList();

        var total = summaries.Sum(s => s.CurrentAmount);
        var invoiced = summaries.Sum(s => s.InvoicedAmount);
        var paid = summaries.Sum(s => s.PaidAmount);
        return Result.Success(new ProjectContractsSummaryDto(
            projectId, contracts.Count, ContractMapper.Money(total), ContractMapper.Money(invoiced), ContractMapper.Money(paid),
            ContractMapper.Money(summaries.Sum(s => s.RemainingCommitment)), ContractMapper.Money(summaries.Sum(s => s.OutstandingPayable)),
            total > 0 ? Math.Round(invoiced * 100m / total, 2) : 0m, total > 0 ? Math.Round(paid * 100m / total, 2) : 0m,
            summaries.Count(s => s.IsOverInvoiced)));
    }

    public async Task<Result<ContractDto>> CreateAsync(CreateContractRequest request, CancellationToken cancellationToken)
    {
        var number = ContractMapper.NormalizeNumber(request.ContractNumber);
        var error = Validate(number, request.Title, request.Counterparty, request.StartDate, request.EndDate, request.OriginalAmount);
        if (error is not null)
        {
            return Result.Failure<ContractDto>(error);
        }

        if (await repository.ContractNumberExistsAsync(request.ProjectId, number, null, cancellationToken))
        {
            return Result.Failure<ContractDto>(Error.Conflict("The project already has a contract with this number."));
        }

        var contract = new Contract(Guid.NewGuid(), request.TenantId, request.ProjectId, number, request.Title, request.Counterparty, request.OriginalAmount);
        contract.UpdateDetails(number, request.Title, request.Counterparty, request.Description, request.SignDate, request.StartDate, request.EndDate, request.OriginalAmount);

        await repository.AddContractAsync(contract, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ContractMapper.ToDto(contract, ContractCalculator.Summarize(contract, [], [])));
    }

    public async Task<Result<ContractDto>> UpdateAsync(Guid id, UpdateContractRequest request, CancellationToken cancellationToken)
    {
        var contract = await repository.GetContractAsync(id, cancellationToken);
        if (contract is null)
        {
            return Result.Failure<ContractDto>(Error.NotFound("Contract not found."));
        }

        // Approvers are looking at this contract as it stands.
        if (contract.ApprovalStatus == ApprovalStatus.PendingApproval)
        {
            return Result.Failure<ContractDto>(Error.Conflict("A contract that is pending approval cannot be edited."));
        }

        // The signed amount is what was approved; changing it afterwards is what an addendum is for.
        if (contract.ApprovalStatus == ApprovalStatus.Approved && request.OriginalAmount != contract.OriginalAmount)
        {
            return Result.Failure<ContractDto>(Error.Conflict("The amount of an approved contract can only change through an addendum."));
        }

        var number = ContractMapper.NormalizeNumber(request.ContractNumber);
        var error = Validate(number, request.Title, request.Counterparty, request.StartDate, request.EndDate, request.OriginalAmount);
        if (error is not null)
        {
            return Result.Failure<ContractDto>(error);
        }

        if (number != contract.ContractNumber && await repository.ContractNumberExistsAsync(contract.ProjectId, number, id, cancellationToken))
        {
            return Result.Failure<ContractDto>(Error.Conflict("The project already has a contract with this number."));
        }

        contract.UpdateDetails(number, request.Title, request.Counterparty, request.Description, request.SignDate, request.StartDate, request.EndDate, request.OriginalAmount);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(contract, cancellationToken));
    }

    public async Task<Result<ContractDto>> ChangeStatusAsync(Guid id, ChangeContractStatusRequest request, CancellationToken cancellationToken)
    {
        var contract = await repository.GetContractAsync(id, cancellationToken);
        if (contract is null)
        {
            return Result.Failure<ContractDto>(Error.NotFound("Contract not found."));
        }

        if (!Enum.IsDefined(request.Status))
        {
            return Result.Failure<ContractDto>(Error.Validation("Unknown status."));
        }

        // A contract only goes live once it has been approved.
        if (request.Status != ContractStatus.Draft && contract.ApprovalStatus != ApprovalStatus.Approved)
        {
            return Result.Failure<ContractDto>(Error.Conflict("Only an approved contract can leave Draft."));
        }

        contract.ChangeStatus(request.Status);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(contract, cancellationToken));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var contract = await repository.GetContractAsync(id, cancellationToken);
        if (contract is null)
        {
            return Result.Failure(Error.NotFound("Contract not found."));
        }

        if (contract.ApprovalStatus == ApprovalStatus.PendingApproval)
        {
            return Result.Failure(Error.Conflict("A contract that is pending approval cannot be deleted."));
        }

        if ((await repository.ListInvoicesAsync(id, cancellationToken)).Count > 0)
        {
            return Result.Failure(Error.Conflict("A contract with invoices cannot be deleted."));
        }

        var addenda = await repository.ListAddendaAsync(id, cancellationToken);
        if (addenda.Any(a => a.ApprovalStatus is ApprovalStatus.Approved or ApprovalStatus.PendingApproval))
        {
            return Result.Failure(Error.Conflict("A contract with approved or pending addenda cannot be deleted."));
        }

        await repository.RemoveAddendaAsync(addenda, cancellationToken);
        await repository.RemoveContractAsync(contract, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ContractDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var contract = await repository.GetContractAsync(id, cancellationToken);
        if (contract is null)
        {
            return Result.Failure<ContractDto>(Error.NotFound("Contract not found."));
        }

        if (contract.ApprovalStatus is ApprovalStatus.PendingApproval or ApprovalStatus.Approved)
        {
            return Result.Failure<ContractDto>(Error.Conflict("The contract has already been submitted for approval."));
        }

        var subject = new ApprovalSubject(SubjectType, contract.Id, contract.TenantId, ScopeType: "Project", ScopeId: contract.ProjectId);
        if (await approvalRequester.RequestApprovalAsync(subject, cancellationToken) == ApprovalRequestOutcome.Submitted)
        {
            contract.MarkPendingApproval();
        }
        else
        {
            // No Workflow installed: apply the direct-approve business rule immediately.
            contract.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(contract, cancellationToken));
    }

    private async Task<ContractDto> ToDtoAsync(Contract contract, CancellationToken cancellationToken) =>
        ContractMapper.ToDto(contract, ContractCalculator.Summarize(
            contract, await repository.ListAddendaAsync(contract.Id, cancellationToken), await repository.ListInvoicesAsync(contract.Id, cancellationToken)));

    private static Error? Validate(string number, string title, string counterparty, DateOnly? start, DateOnly? end, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > MaxNumberLength)
        {
            return Error.Validation($"The contract number is required and at most {MaxNumberLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("Title is required.");
        }

        if (string.IsNullOrWhiteSpace(counterparty))
        {
            return Error.Validation("The other party is required.");
        }

        if (start is not null && end is not null && end < start)
        {
            return Error.Validation("The end date cannot be before the start date.");
        }

        return amount is < 0 or > ContractMapper.MaxAmount
            ? Error.Validation($"The amount must be between 0 and {ContractMapper.MaxAmount:N0} rials.")
            : null;
    }
}
