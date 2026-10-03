using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public sealed class ContractInvoiceService(
    IContractRepository repository,
    IContractsUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IContractInvoiceService
{
    public const string SubjectType = "ContractInvoice";

    public async Task<Result<IReadOnlyList<ContractInvoiceDto>>> ListAsync(Guid contractId, CancellationToken cancellationToken)
    {
        if (await repository.GetContractAsync(contractId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<ContractInvoiceDto>>(Error.NotFound("Contract not found."));
        }

        var invoices = await repository.ListInvoicesAsync(contractId, cancellationToken);
        return Result.Success<IReadOnlyList<ContractInvoiceDto>>(
            invoices.OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNumber, StringComparer.Ordinal).Select(ContractMapper.ToDto).ToList());
    }

    public async Task<Result<ContractInvoiceDto>> CreateAsync(Guid contractId, CreateContractInvoiceRequest request, CancellationToken cancellationToken)
    {
        var contract = await repository.GetContractAsync(contractId, cancellationToken);
        if (contract is null)
        {
            return Result.Failure<ContractInvoiceDto>(Error.NotFound("Contract not found."));
        }

        var number = ContractMapper.NormalizeNumber(request.InvoiceNumber);
        var error = ContractIsOpen(contract) ?? Validate(number, request.Amount)
            ?? await CheckFitsAsync(contract, request.Amount, null, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<ContractInvoiceDto>(error);
        }

        if (await repository.InvoiceNumberExistsAsync(contractId, number, null, cancellationToken))
        {
            return Result.Failure<ContractInvoiceDto>(Error.Conflict("The contract already has an invoice with this number."));
        }

        var invoice = new ContractInvoice(Guid.NewGuid(), request.TenantId, contractId, number, request.InvoiceDate, request.Amount, request.Description);
        await repository.AddInvoiceAsync(invoice, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ContractMapper.ToDto(invoice));
    }

    public async Task<Result<ContractInvoiceDto>> UpdateAsync(Guid contractId, Guid invoiceId, UpdateContractInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await GetOwnedAsync(contractId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<ContractInvoiceDto>(Error.NotFound("Invoice not found."));
        }

        if (invoice.ApprovalStatus is ApprovalStatus.PendingApproval or ApprovalStatus.Approved)
        {
            return Result.Failure<ContractInvoiceDto>(Error.Conflict("An invoice that is pending approval or approved cannot be edited."));
        }

        var contract = (await repository.GetContractAsync(contractId, cancellationToken))!;
        var number = ContractMapper.NormalizeNumber(request.InvoiceNumber);
        var error = Validate(number, request.Amount) ?? await CheckFitsAsync(contract, request.Amount, invoice.Id, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<ContractInvoiceDto>(error);
        }

        if (number != invoice.InvoiceNumber && await repository.InvoiceNumberExistsAsync(contractId, number, invoice.Id, cancellationToken))
        {
            return Result.Failure<ContractInvoiceDto>(Error.Conflict("The contract already has an invoice with this number."));
        }

        invoice.UpdateDetails(number, request.InvoiceDate, request.Amount, request.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ContractMapper.ToDto(invoice));
    }

    public async Task<Result> DeleteAsync(Guid contractId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await GetOwnedAsync(contractId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure(Error.NotFound("Invoice not found."));
        }

        if (invoice.ApprovalStatus is ApprovalStatus.PendingApproval or ApprovalStatus.Approved || invoice.PaidAmount > 0)
        {
            return Result.Failure(Error.Conflict("An invoice that is pending approval, approved or paid cannot be deleted."));
        }

        await repository.RemoveInvoiceAsync(invoice, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ContractInvoiceDto>> SubmitForApprovalAsync(Guid contractId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await GetOwnedAsync(contractId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<ContractInvoiceDto>(Error.NotFound("Invoice not found."));
        }

        if (invoice.ApprovalStatus is ApprovalStatus.PendingApproval or ApprovalStatus.Approved)
        {
            return Result.Failure<ContractInvoiceDto>(Error.Conflict("The invoice has already been submitted for approval."));
        }

        var contract = (await repository.GetContractAsync(contractId, cancellationToken))!;
        var error = ContractIsOpen(contract) ?? await CheckFitsAsync(contract, invoice.Amount, invoice.Id, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<ContractInvoiceDto>(error);
        }

        var subject = new ApprovalSubject(SubjectType, invoice.Id, invoice.TenantId, ScopeType: "Project", ScopeId: contract.ProjectId);
        if (await approvalRequester.RequestApprovalAsync(subject, cancellationToken) == ApprovalRequestOutcome.Submitted)
        {
            invoice.MarkPendingApproval();
        }
        else
        {
            invoice.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ContractMapper.ToDto(invoice));
    }

    public async Task<Result<ContractInvoiceDto>> RecordPaymentAsync(Guid contractId, Guid invoiceId, RecordInvoicePaymentRequest request, CancellationToken cancellationToken)
    {
        var invoice = await GetOwnedAsync(contractId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<ContractInvoiceDto>(Error.NotFound("Invoice not found."));
        }

        // Money only moves against an invoice that has been accepted.
        if (invoice.ApprovalStatus != ApprovalStatus.Approved)
        {
            return Result.Failure<ContractInvoiceDto>(Error.Conflict("Payments can only be recorded against an approved invoice."));
        }

        if (request.PaidAmount < 0 || request.PaidAmount > invoice.Amount)
        {
            return Result.Failure<ContractInvoiceDto>(Error.Validation("The paid amount must be between 0 and the invoice amount."));
        }

        if (request.PaidAmount > 0 && request.PaymentDate is null)
        {
            return Result.Failure<ContractInvoiceDto>(Error.Validation("The payment date is required."));
        }

        if (request.PaymentDate is not null && request.PaymentDate < invoice.InvoiceDate)
        {
            return Result.Failure<ContractInvoiceDto>(Error.Validation("The payment date cannot be before the invoice date."));
        }

        invoice.RecordPayment(request.PaidAmount, request.PaymentDate);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ContractMapper.ToDto(invoice));
    }

    private async Task<ContractInvoice?> GetOwnedAsync(Guid contractId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await repository.GetInvoiceAsync(invoiceId, cancellationToken);
        return invoice is not null && invoice.ContractId == contractId ? invoice : null;
    }

    /// <summary>Invoices are billed against an approved contract that has not been terminated.</summary>
    private static Error? ContractIsOpen(Contract contract)
    {
        if (contract.ApprovalStatus != ApprovalStatus.Approved)
        {
            return Error.Conflict("Invoices can only be raised against an approved contract.");
        }

        return contract.Status == ContractStatus.Terminated
            ? Error.Conflict("A terminated contract cannot be invoiced.")
            : null;
    }

    /// <summary>The amount, together with the invoices already raised (not counting this one), must stay within the contract's value.</summary>
    private async Task<Error?> CheckFitsAsync(Contract contract, decimal amount, Guid? excludeInvoiceId, CancellationToken cancellationToken)
    {
        var addenda = await repository.ListAddendaAsync(contract.Id, cancellationToken);
        var invoices = (await repository.ListInvoicesAsync(contract.Id, cancellationToken)).Where(i => i.Id != excludeInvoiceId);
        return amount > ContractCalculator.InvoiceableAmount(contract, addenda, invoices)
            ? Error.Conflict("The invoice would take the total invoiced above the contract amount.")
            : null;
    }

    private static Error? Validate(string number, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > ContractService.MaxNumberLength)
        {
            return Error.Validation($"The invoice number is required and at most {ContractService.MaxNumberLength} characters.");
        }

        return amount <= 0 || amount > ContractMapper.MaxAmount
            ? Error.Validation($"The amount must be above 0 and at most {ContractMapper.MaxAmount:N0} rials.")
            : null;
    }
}
