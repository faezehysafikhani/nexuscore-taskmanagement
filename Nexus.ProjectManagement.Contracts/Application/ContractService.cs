using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public sealed class ContractService(
    IContractRepository repository,
    IContractsUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester,
    ISupplierRepository? suppliers = null) : IContractService
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

    public async Task<Result<IReadOnlyList<ContractDetailDto>>> ListForProjectsAsync(ListContractsForProjectsRequest request, CancellationToken cancellationToken)
    {
        var projectIds = request.ProjectIds.Distinct().ToList();
        if (projectIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<ContractDetailDto>>([]);
        }

        var contracts = await repository.ListContractsForProjectsAsync(projectIds, cancellationToken);
        var ids = contracts.Select(c => c.Id).ToList();
        var addenda = (await repository.ListAddendaAsync(ids, cancellationToken)).GroupBy(a => a.ContractId).ToDictionary(g => g.Key, g => g.ToList());
        var invoices = (await repository.ListInvoicesAsync(ids, cancellationToken)).GroupBy(i => i.ContractId).ToDictionary(g => g.Key, g => g.ToList());

        return Result.Success<IReadOnlyList<ContractDetailDto>>(contracts
            .OrderBy(c => c.ProjectId).ThenBy(c => c.ContractNumber, StringComparer.Ordinal)
            .Select(c =>
            {
                var contractAddenda = addenda.GetValueOrDefault(c.Id) ?? [];
                var contractInvoices = invoices.GetValueOrDefault(c.Id) ?? [];
                return new ContractDetailDto(
                    ContractMapper.ToDto(c, ContractCalculator.Summarize(c, contractAddenda, contractInvoices)),
                    contractAddenda.OrderBy(a => a.Number).Select(ContractMapper.ToDto).ToList(),
                    contractInvoices.OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNumber, StringComparer.Ordinal).Select(ContractMapper.ToDto).ToList());
            })
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
            summaries.Count(s => s.IsOverInvoiced),
            ContractMapper.Money(total - summaries.Sum(s => s.ApprovedAddendaAmount)),
            ContractMapper.Money(summaries.Sum(s => s.ApprovedAddendaAmount))));
    }

    public async Task<Result<ContractDto>> CreateAsync(CreateContractRequest request, CancellationToken cancellationToken)
    {
        var number = ContractMapper.NormalizeNumber(request.ContractNumber);
        var error = Validate(number, request.Title, request.SupplierId is { } chosen && chosen != Guid.Empty ? "-" : request.Counterparty, request.StartDate, request.EndDate, request.OriginalAmount);
        if (error is not null)
        {
            return Result.Failure<ContractDto>(error);
        }

        if (await repository.ContractNumberExistsAsync(request.ProjectId, number, null, cancellationToken))
        {
            return Result.Failure<ContractDto>(Error.Conflict("The project already has a contract with this number."));
        }

        var party = await ResolveSupplierAsync(request.TenantId, request.SupplierId, request.Counterparty, cancellationToken);
        if (party.IsFailure)
        {
            return Result.Failure<ContractDto>(party.Error!);
        }

        var contract = new Contract(Guid.NewGuid(), request.TenantId, request.ProjectId, number, request.Title, party.Value!.Name, request.OriginalAmount);
        contract.UpdateDetails(number, request.Title, party.Value!.Name, request.Description, request.SignDate, request.StartDate, request.EndDate, request.OriginalAmount);
        contract.SetParties(request.SupervisorName, request.BeneficiaryName);
        contract.SetSupplier(party.Value!.SupplierId);

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
        var unlink = request.SupplierId == Guid.Empty;
        var error = Validate(number, request.Title, request.SupplierId is { } chosen && chosen != Guid.Empty ? "-" : request.Counterparty, request.StartDate, request.EndDate, request.OriginalAmount);
        if (error is not null)
        {
            return Result.Failure<ContractDto>(error);
        }

        if (number != contract.ContractNumber && await repository.ContractNumberExistsAsync(contract.ProjectId, number, id, cancellationToken))
        {
            return Result.Failure<ContractDto>(Error.Conflict("The project already has a contract with this number."));
        }

        // A supplier id picks that supplier; the empty id unlinks (the typed name stays); no id follows the typed name.
        var party = unlink
            ? Result.Success(new Party(null, request.Counterparty.Trim()))
            : await ResolveSupplierAsync(contract.TenantId, request.SupplierId, request.Counterparty, cancellationToken);
        if (party.IsFailure)
        {
            return Result.Failure<ContractDto>(party.Error!);
        }

        contract.UpdateDetails(number, request.Title, party.Value!.Name, request.Description, request.SignDate, request.StartDate, request.EndDate, request.OriginalAmount);
        contract.SetSupplier(party.Value!.SupplierId);
        // Omitted (null) keeps what is stored, so a client that predates these fields can still edit a contract; an empty text clears it.
        if (request.SupervisorName is not null || request.BeneficiaryName is not null)
        {
            contract.SetParties(request.SupervisorName ?? contract.SupervisorName, request.BeneficiaryName ?? contract.BeneficiaryName);
        }

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

    private sealed record Party(Guid? SupplierId, string Name);

    /// <summary>
    /// Who the contract is with. A chosen supplier must exist in the tenant and be active, and its name becomes the contract's. Without one
    /// the typed name is matched to the tenant's supplier of that name (so an older client that only sends a name still ends up linked),
    /// and a supplier is created for a name nobody has used yet. With no supplier store registered the name is kept as it is, unlinked.
    /// </summary>
    private async Task<Result<Party>> ResolveSupplierAsync(Guid tenantId, Guid? supplierId, string counterparty, CancellationToken cancellationToken)
    {
        if (suppliers is null)
        {
            return Result.Success(new Party(null, counterparty.Trim()));
        }

        if (supplierId is { } id && id != Guid.Empty)
        {
            var chosen = await suppliers.GetAsync(id, cancellationToken);
            if (chosen is null || chosen.TenantId != tenantId)
            {
                return Result.Failure<Party>(Error.NotFound("Supplier not found."));
            }

            return chosen.Status == SupplierStatus.Inactive
                ? Result.Failure<Party>(Error.Conflict("The supplier is inactive."))
                : Result.Success(new Party(chosen.Id, chosen.Name));
        }

        var existing = await suppliers.FindByNormalizedNameAsync(tenantId, SupplierNames.Normalize(counterparty), null, cancellationToken);
        if (existing is not null)
        {
            return Result.Success(new Party(existing.Id, existing.Name));
        }

        var created = new Supplier(Guid.NewGuid(), tenantId, counterparty, SupplierCategory.Other);
        await suppliers.AddAsync(created, cancellationToken);
        return Result.Success(new Party(created.Id, created.Name));
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
