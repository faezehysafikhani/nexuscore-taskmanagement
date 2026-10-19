using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Application.Persian;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public sealed class ContractCommitmentService(
    IContractRepository contracts,
    ICommitmentRepository commitments,
    IContractsUnitOfWork unitOfWork,
    TimeProvider? clock = null) : IContractCommitmentService
{
    private DateOnly Today => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    // ---- milestones ----

    public async Task<Result<IReadOnlyList<ContractMilestoneDto>>> ListMilestonesAsync(Guid contractId, CancellationToken cancellationToken)
    {
        if (await contracts.GetContractAsync(contractId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<ContractMilestoneDto>>(Error.NotFound("Contract not found."));
        }

        var items = await commitments.ListMilestonesAsync(contractId, cancellationToken);
        return Result.Success<IReadOnlyList<ContractMilestoneDto>>(items.OrderBy(m => m.DueDate).ThenBy(m => m.Title, StringComparer.CurrentCulture).Select(ToDto).ToList());
    }

    public async Task<Result<ContractMilestoneDto>> CreateMilestoneAsync(Guid contractId, SaveContractMilestoneRequest request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetContractAsync(contractId, cancellationToken);
        if (contract is null)
        {
            return Result.Failure<ContractMilestoneDto>(Error.NotFound("Contract not found."));
        }

        var error = ValidateMilestone(request);
        if (error is not null)
        {
            return Result.Failure<ContractMilestoneDto>(error);
        }

        var milestone = new ContractMilestone(Guid.NewGuid(), contract.TenantId, contractId, request.Title, request.DueDate);
        milestone.Update(request.Title, request.Description, request.DueDate, request.Status, request.CompletedDate, Today);
        await commitments.AddMilestoneAsync(milestone, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(milestone));
    }

    public async Task<Result<ContractMilestoneDto>> UpdateMilestoneAsync(Guid contractId, Guid id, SaveContractMilestoneRequest request, CancellationToken cancellationToken)
    {
        var milestone = await commitments.GetMilestoneAsync(id, cancellationToken);
        if (milestone is null || milestone.ContractId != contractId)
        {
            return Result.Failure<ContractMilestoneDto>(Error.NotFound("Milestone not found."));
        }

        var error = ValidateMilestone(request);
        if (error is not null)
        {
            return Result.Failure<ContractMilestoneDto>(error);
        }

        milestone.Update(request.Title, request.Description, request.DueDate, request.Status, request.CompletedDate, Today);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(milestone));
    }

    public async Task<Result> DeleteMilestoneAsync(Guid contractId, Guid id, CancellationToken cancellationToken)
    {
        var milestone = await commitments.GetMilestoneAsync(id, cancellationToken);
        if (milestone is null || milestone.ContractId != contractId)
        {
            return Result.Failure(Error.NotFound("Milestone not found."));
        }

        await commitments.RemoveMilestoneAsync(milestone, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ---- obligations ----

    public async Task<Result<IReadOnlyList<ContractObligationDto>>> ListObligationsAsync(Guid contractId, CancellationToken cancellationToken)
    {
        if (await contracts.GetContractAsync(contractId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<ContractObligationDto>>(Error.NotFound("Contract not found."));
        }

        var items = await commitments.ListObligationsAsync(contractId, cancellationToken);
        return Result.Success<IReadOnlyList<ContractObligationDto>>(items
            .OrderBy(o => o.DueDate is null).ThenBy(o => o.DueDate).ThenBy(o => o.Title, StringComparer.CurrentCulture)
            .Select(ToDto).ToList());
    }

    public async Task<Result<ContractObligationDto>> CreateObligationAsync(Guid contractId, SaveContractObligationRequest request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetContractAsync(contractId, cancellationToken);
        if (contract is null)
        {
            return Result.Failure<ContractObligationDto>(Error.NotFound("Contract not found."));
        }

        var error = ValidateObligation(request);
        if (error is not null)
        {
            return Result.Failure<ContractObligationDto>(error);
        }

        var obligation = new ContractObligation(Guid.NewGuid(), contract.TenantId, contractId, request.Title, request.Party);
        obligation.Update(request.Title, request.Description, request.Party, request.DueDate, request.Status, request.FulfilledDate, Today);
        await commitments.AddObligationAsync(obligation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(obligation));
    }

    public async Task<Result<ContractObligationDto>> UpdateObligationAsync(Guid contractId, Guid id, SaveContractObligationRequest request, CancellationToken cancellationToken)
    {
        var obligation = await commitments.GetObligationAsync(id, cancellationToken);
        if (obligation is null || obligation.ContractId != contractId)
        {
            return Result.Failure<ContractObligationDto>(Error.NotFound("Obligation not found."));
        }

        var error = ValidateObligation(request);
        if (error is not null)
        {
            return Result.Failure<ContractObligationDto>(error);
        }

        obligation.Update(request.Title, request.Description, request.Party, request.DueDate, request.Status, request.FulfilledDate, Today);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(obligation));
    }

    public async Task<Result> DeleteObligationAsync(Guid contractId, Guid id, CancellationToken cancellationToken)
    {
        var obligation = await commitments.GetObligationAsync(id, cancellationToken);
        if (obligation is null || obligation.ContractId != contractId)
        {
            return Result.Failure(Error.NotFound("Obligation not found."));
        }

        await commitments.RemoveObligationAsync(obligation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ContractCommitmentsSummaryDto>> GetSummaryAsync(Guid contractId, CancellationToken cancellationToken)
    {
        if (await contracts.GetContractAsync(contractId, cancellationToken) is null)
        {
            return Result.Failure<ContractCommitmentsSummaryDto>(Error.NotFound("Contract not found."));
        }

        var today = Today;
        var milestones = await commitments.ListMilestonesAsync(contractId, cancellationToken);
        var obligations = await commitments.ListObligationsAsync(contractId, cancellationToken);
        return Result.Success(new ContractCommitmentsSummaryDto(
            milestones.Count, milestones.Count(m => m.Status == ContractMilestoneStatus.Completed), milestones.Count(m => m.IsOverdue(today)),
            obligations.Count, obligations.Count(o => o.Status == ObligationStatus.Fulfilled), obligations.Count(o => o.IsOverdue(today)),
            obligations.Count(o => o.Status == ObligationStatus.Breached)));
    }

    // ---- shared ----

    private ContractMilestoneDto ToDto(ContractMilestone m) => new(
        m.Id, m.ContractId, m.Title, m.Description, m.DueDate, PersianDates.Format(m.DueDate)!, m.Status,
        m.CompletedDate, PersianDates.Format(m.CompletedDate), m.IsOverdue(Today), m.CreatedByUserId);

    private ContractObligationDto ToDto(ContractObligation o) => new(
        o.Id, o.ContractId, o.Title, o.Description, o.Party, o.DueDate, PersianDates.Format(o.DueDate),
        o.Status, o.FulfilledDate, PersianDates.Format(o.FulfilledDate), o.IsOverdue(Today), o.CreatedByUserId);

    private static Error? ValidateMilestone(SaveContractMilestoneRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 300)
        {
            return Error.Validation("The title is required and at most 300 characters.");
        }

        if (request.Description is { Length: > 2000 })
        {
            return Error.Validation("The description is at most 2000 characters.");
        }

        return Enum.IsDefined(request.Status) ? null : Error.Validation("Unknown status.");
    }

    private static Error? ValidateObligation(SaveContractObligationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 300)
        {
            return Error.Validation("The title is required and at most 300 characters.");
        }

        if (request.Description is { Length: > 2000 })
        {
            return Error.Validation("The description is at most 2000 characters.");
        }

        return Enum.IsDefined(request.Status) && Enum.IsDefined(request.Party) ? null : Error.Validation("Unknown status or party.");
    }
}
