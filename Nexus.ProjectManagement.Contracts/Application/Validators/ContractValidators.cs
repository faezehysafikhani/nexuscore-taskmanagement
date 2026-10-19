using FluentValidation;
using Nexus.ProjectManagement.Contracts.Application.Dtos;

namespace Nexus.ProjectManagement.Contracts.Application.Validators;

public sealed class CreateContractRequestValidator : AbstractValidator<CreateContractRequest>
{
    public CreateContractRequestValidator()
    {
        RuleFor(x => x.ContractNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Counterparty).NotEmpty().When(x => x.SupplierId is null || x.SupplierId == Guid.Empty).MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.ScopeOfWork).MaximumLength(4000);
        RuleFor(x => x.OriginalAmount).InclusiveBetween(0, ContractMapper.MaxAmount);
        RuleFor(x => x.SupervisorName).MaximumLength(300);
        RuleFor(x => x.BeneficiaryName).MaximumLength(300);
    }
}

public sealed class UpdateContractRequestValidator : AbstractValidator<UpdateContractRequest>
{
    public UpdateContractRequestValidator()
    {
        RuleFor(x => x.ContractNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Counterparty).NotEmpty().When(x => x.SupplierId is null || x.SupplierId == Guid.Empty).MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.ScopeOfWork).MaximumLength(4000);
        RuleFor(x => x.OriginalAmount).InclusiveBetween(0, ContractMapper.MaxAmount);
        RuleFor(x => x.SupervisorName).MaximumLength(300);
        RuleFor(x => x.BeneficiaryName).MaximumLength(300);
    }
}

public sealed class CreateContractAddendumRequestValidator : AbstractValidator<CreateContractAddendumRequest>
{
    public CreateContractAddendumRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(4000);
    }
}

public sealed class UpdateContractAddendumRequestValidator : AbstractValidator<UpdateContractAddendumRequest>
{
    public UpdateContractAddendumRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(4000);
    }
}

public sealed class CreateContractInvoiceRequestValidator : AbstractValidator<CreateContractInvoiceRequest>
{
    public CreateContractInvoiceRequestValidator()
    {
        RuleFor(x => x.InvoiceNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Title).MaximumLength(300);
    }
}

public sealed class UpdateContractInvoiceRequestValidator : AbstractValidator<UpdateContractInvoiceRequest>
{
    public UpdateContractInvoiceRequestValidator()
    {
        RuleFor(x => x.InvoiceNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Title).MaximumLength(300);
    }
}

public sealed class CreateSupplierRequestValidator : AbstractValidator<CreateSupplierRequest>
{
    public CreateSupplierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.NationalId).MaximumLength(50);
        RuleFor(x => x.ContactPerson).MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Email).MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(1000);
        RuleFor(x => x.Description).MaximumLength(4000);
    }
}

public sealed class UpdateSupplierRequestValidator : AbstractValidator<UpdateSupplierRequest>
{
    public UpdateSupplierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.NationalId).MaximumLength(50);
        RuleFor(x => x.ContactPerson).MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Email).MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(1000);
        RuleFor(x => x.Description).MaximumLength(4000);
    }
}
