using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public sealed class SupplierService(
    ISupplierRepository suppliers,
    IContractRepository contracts,
    IContractsUnitOfWork unitOfWork) : ISupplierService
{
    public async Task<Result<IReadOnlyList<SupplierDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var all = await suppliers.ListAsync(tenantId, cancellationToken);
        var loaded = await LoadContractsAsync(tenantId, supplierId: null, cancellationToken);
        var bySupplier = loaded.Where(l => l.Contract.SupplierId is not null).ToLookup(l => l.Contract.SupplierId!.Value);

        return Result.Success<IReadOnlyList<SupplierDto>>(all
            .OrderBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(s => ToDto(s, bySupplier[s.Id].ToList()))
            .ToList());
    }

    public async Task<Result<SupplierDetailDto>> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.GetAsync(id, cancellationToken);
        if (supplier is null || supplier.TenantId != tenantId)
        {
            return Result.Failure<SupplierDetailDto>(Error.NotFound("Supplier not found."));
        }

        var loaded = await LoadContractsAsync(tenantId, id, cancellationToken);
        var dtos = loaded.Select(l => l.Dto).OrderBy(c => c.ProjectId).ThenBy(c => c.ContractNumber, StringComparer.Ordinal).ToList();
        return Result.Success(new SupplierDetailDto(ToDto(supplier, loaded), dtos));
    }

    public async Task<Result<SupplierDto>> CreateAsync(Guid tenantId, CreateSupplierRequest request, CancellationToken cancellationToken)
    {
        var error = Validate(request.Name, request.Category, SupplierStatus.Active);
        if (error is not null)
        {
            return Result.Failure<SupplierDto>(error);
        }

        if (await suppliers.FindByNormalizedNameAsync(tenantId, SupplierNames.Normalize(request.Name), null, cancellationToken) is not null)
        {
            return Result.Failure<SupplierDto>(Error.Conflict("A supplier with this name already exists."));
        }

        var supplier = new Supplier(Guid.NewGuid(), tenantId, request.Name, request.Category);
        supplier.Update(request.Name, request.Category, SupplierStatus.Active, request.NationalId, request.ContactPerson, request.Phone, request.Email, request.Address, request.Description);
        await suppliers.AddAsync(supplier, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(supplier, []));
    }

    public async Task<Result<SupplierDto>> UpdateAsync(Guid tenantId, Guid id, UpdateSupplierRequest request, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.GetAsync(id, cancellationToken);
        if (supplier is null || supplier.TenantId != tenantId)
        {
            return Result.Failure<SupplierDto>(Error.NotFound("Supplier not found."));
        }

        var error = Validate(request.Name, request.Category, request.Status);
        if (error is not null)
        {
            return Result.Failure<SupplierDto>(error);
        }

        var normalized = SupplierNames.Normalize(request.Name);
        if (normalized != supplier.NormalizedName
            && await suppliers.FindByNormalizedNameAsync(tenantId, normalized, id, cancellationToken) is not null)
        {
            return Result.Failure<SupplierDto>(Error.Conflict("A supplier with this name already exists."));
        }

        var renamed = request.Name.Trim() != supplier.Name;
        supplier.Update(request.Name, request.Category, request.Status, request.NationalId, request.ContactPerson, request.Phone, request.Email, request.Address, request.Description);

        // The contracts carry the name as text too; keep it the supplier's.
        if (renamed)
        {
            foreach (var contract in await suppliers.ListContractsAsync(tenantId, id, cancellationToken))
            {
                contract.SetCounterparty(supplier.Name);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(supplier, await LoadContractsAsync(tenantId, id, cancellationToken)));
    }

    public async Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.GetAsync(id, cancellationToken);
        if (supplier is null || supplier.TenantId != tenantId)
        {
            return Result.Failure(Error.NotFound("Supplier not found."));
        }

        if ((await suppliers.ListContractsAsync(tenantId, id, cancellationToken)).Count > 0)
        {
            return Result.Failure(Error.Conflict("A supplier with contracts cannot be deleted."));
        }

        await suppliers.RemoveAsync(supplier, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private sealed record Loaded(Contract Contract, ContractDto Dto);

    private async Task<IReadOnlyList<Loaded>> LoadContractsAsync(Guid tenantId, Guid? supplierId, CancellationToken cancellationToken)
    {
        var list = await suppliers.ListContractsAsync(tenantId, supplierId, cancellationToken);
        var ids = list.Select(c => c.Id).ToList();
        var addenda = (await contracts.ListAddendaAsync(ids, cancellationToken)).GroupBy(a => a.ContractId).ToDictionary(g => g.Key, g => g.ToList());
        var invoices = (await contracts.ListInvoicesAsync(ids, cancellationToken)).GroupBy(i => i.ContractId).ToDictionary(g => g.Key, g => g.ToList());
        return list
            .Select(c => new Loaded(c, ContractMapper.ToDto(c, ContractCalculator.Summarize(c, addenda.GetValueOrDefault(c.Id) ?? [], invoices.GetValueOrDefault(c.Id) ?? []))))
            .ToList();
    }

    private static SupplierDto ToDto(Supplier s, IReadOnlyList<Loaded> own)
    {
        // Money is counted from approved contracts only (a draft or rejected one is not a commitment).
        var approved = own.Where(l => l.Dto.ApprovalStatus == ApprovalStatus.Approved).Select(l => l.Dto.Summary).ToList();
        var stats = new SupplierStatsDto(
            own.Count,
            own.Count(l => l.Contract.Status == ContractStatus.Active),
            ContractMapper.Money(approved.Sum(x => x.CurrentAmount.Rials)),
            ContractMapper.Money(approved.Sum(x => x.InvoicedAmount.Rials)),
            ContractMapper.Money(approved.Sum(x => x.PaidAmount.Rials)),
            ContractMapper.Money(approved.Sum(x => x.OutstandingPayable.Rials)));

        return new SupplierDto(
            s.Id, s.TenantId, s.Name, s.Category, s.Status, s.NationalId, s.ContactPerson, s.Phone, s.Email, s.Address, s.Description,
            stats, s.CreatedByUserId);
    }

    private static Error? Validate(string name, SupplierCategory category, SupplierStatus status)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 300)
        {
            return Error.Validation("The supplier name is required and at most 300 characters.");
        }

        return Enum.IsDefined(category) && Enum.IsDefined(status) ? null : Error.Validation("Unknown category or status.");
    }
}
