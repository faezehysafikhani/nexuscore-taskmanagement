using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Application.Persian;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public sealed record ChangeRequestDto(
    Guid Id, Guid TenantId, Guid ProjectId, int Number, string Code, string Title, string? Description, string? Reason,
    ChangeRequestType Type, ChangeRequestPriority Priority, string? RequestedBy, Guid? ContractId,
    string? ScopeImpact, int ScheduleImpactDays, MoneyDto CostImpact, string? OtherImpact, bool HasImpactAssessment,
    ChangeRequestStatus Status, DateTimeOffset? SubmittedAtUtc, DateTimeOffset? DecidedAtUtc, string? DecisionNote,
    DateOnly? ImplementedDate, string? ImplementedDateFa, string? OutcomeNote, DateTimeOffset CreatedAtUtc, Guid? CreatedByUserId);

/// <summary>ScheduleImpactDays and CostImpact (rials) are negative for a saving. The impact on scope is required before submitting.</summary>
public sealed record SaveChangeRequestRequest(
    string Title, ChangeRequestType Type = ChangeRequestType.Scope, ChangeRequestPriority Priority = ChangeRequestPriority.Normal,
    string? Description = null, string? Reason = null, string? RequestedBy = null, Guid? ContractId = null,
    string? ScopeImpact = null, int ScheduleImpactDays = 0, decimal CostImpact = 0, string? OtherImpact = null);

public sealed record CreateChangeRequestRequest(
    Guid TenantId, Guid ProjectId, string Title, ChangeRequestType Type = ChangeRequestType.Scope, ChangeRequestPriority Priority = ChangeRequestPriority.Normal,
    string? Description = null, string? Reason = null, string? RequestedBy = null, Guid? ContractId = null,
    string? ScopeImpact = null, int ScheduleImpactDays = 0, decimal CostImpact = 0, string? OtherImpact = null)
{
    public SaveChangeRequestRequest Details => new(Title, Type, Priority, Description, Reason, RequestedBy, ContractId, ScopeImpact, ScheduleImpactDays, CostImpact, OtherImpact);
}

public sealed record ImplementChangeRequestRequest(DateOnly? Date = null, string? Note = null);

public sealed record CancelChangeRequestRequest(string? Note = null);

public interface IChangeRequestRepository
{
    Task<ChangeRequest?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChangeRequest>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<int> MaxNumberAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddAsync(ChangeRequest changeRequest, CancellationToken cancellationToken);
    Task RemoveAsync(ChangeRequest changeRequest, CancellationToken cancellationToken);
}

public interface IChangeRequestService
{
    Task<Result<IReadOnlyList<ChangeRequestDto>>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<ChangeRequestDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ChangeRequestDto>> CreateAsync(CreateChangeRequestRequest request, CancellationToken cancellationToken);
    Task<Result<ChangeRequestDto>> UpdateAsync(Guid id, SaveChangeRequestRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ChangeRequestDto>> SubmitAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ChangeRequestDto>> ImplementAsync(Guid id, ImplementChangeRequestRequest request, CancellationToken cancellationToken);
    Task<Result<ChangeRequestDto>> CancelAsync(Guid id, CancelChangeRequestRequest request, CancellationToken cancellationToken);
}

/// <summary>Change requests of a project: write one with its impact, submit it for a decision, then carry it out or cancel it.</summary>
public sealed class ChangeRequestService(
    IChangeRequestRepository changes,
    IContractRepository contracts,
    IContractsUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester,
    TimeProvider? clock = null) : IChangeRequestService
{
    public const string SubjectType = "ChangeRequest";

    private TimeProvider Clock => clock ?? TimeProvider.System;

    public async Task<Result<IReadOnlyList<ChangeRequestDto>>> ListAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var items = await changes.ListAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<ChangeRequestDto>>(items.OrderByDescending(c => c.Number).Select(ToDto).ToList());
    }

    public async Task<Result<ChangeRequestDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await changes.GetAsync(id, cancellationToken) is { } item
            ? Result.Success(ToDto(item))
            : Result.Failure<ChangeRequestDto>(Error.NotFound("Change request not found."));

    public async Task<Result<ChangeRequestDto>> CreateAsync(CreateChangeRequestRequest request, CancellationToken cancellationToken)
    {
        var error = await ValidateAsync(request.ProjectId, request.Details, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<ChangeRequestDto>(error);
        }

        var number = await changes.MaxNumberAsync(request.ProjectId, cancellationToken) + 1;
        var item = new ChangeRequest(Guid.NewGuid(), request.TenantId, request.ProjectId, number, request.Details.Title);
        Apply(item, request.Details);
        await changes.AddAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    public async Task<Result<ChangeRequestDto>> UpdateAsync(Guid id, SaveChangeRequestRequest request, CancellationToken cancellationToken)
    {
        var item = await changes.GetAsync(id, cancellationToken);
        if (item is null)
        {
            return Result.Failure<ChangeRequestDto>(Error.NotFound("Change request not found."));
        }

        // What was decided on, or is being decided on, stays as it was.
        if (item.Status is not (ChangeRequestStatus.Draft or ChangeRequestStatus.Rejected))
        {
            return Result.Failure<ChangeRequestDto>(Error.Conflict("Only a draft or a rejected change request can be edited."));
        }

        var error = await ValidateAsync(item.ProjectId, request, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<ChangeRequestDto>(error);
        }

        Apply(item, request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await changes.GetAsync(id, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("Change request not found."));
        }

        if (item.Status != ChangeRequestStatus.Draft)
        {
            return Result.Failure(Error.Conflict("Only a draft change request can be deleted; cancel it instead."));
        }

        await changes.RemoveAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ChangeRequestDto>> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await changes.GetAsync(id, cancellationToken);
        if (item is null)
        {
            return Result.Failure<ChangeRequestDto>(Error.NotFound("Change request not found."));
        }

        if (item.Status != ChangeRequestStatus.Draft)
        {
            return Result.Failure<ChangeRequestDto>(Error.Conflict("Only a draft change request can be submitted."));
        }

        // The impact is written down before anyone decides.
        if (!item.HasImpactAssessment)
        {
            return Result.Failure<ChangeRequestDto>(Error.Validation("Describe the impact on scope before submitting the change request."));
        }

        var now = Clock.GetUtcNow();
        var subject = new ApprovalSubject(SubjectType, item.Id, item.TenantId, ScopeType: "Project", ScopeId: item.ProjectId);
        if (await approvalRequester.RequestApprovalAsync(subject, cancellationToken) == ApprovalRequestOutcome.Submitted)
        {
            item.MarkSubmitted(now);
        }
        else
        {
            // No Workflow installed: approved as soon as it is submitted, as the other project records are.
            item.MarkSubmitted(now);
            item.Approve(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    public async Task<Result<ChangeRequestDto>> ImplementAsync(Guid id, ImplementChangeRequestRequest request, CancellationToken cancellationToken)
    {
        var item = await changes.GetAsync(id, cancellationToken);
        if (item is null)
        {
            return Result.Failure<ChangeRequestDto>(Error.NotFound("Change request not found."));
        }

        if (item.Status != ChangeRequestStatus.Approved)
        {
            return Result.Failure<ChangeRequestDto>(Error.Conflict("Only an approved change request can be marked as implemented."));
        }

        if (request.Note is { Length: > 2000 })
        {
            return Result.Failure<ChangeRequestDto>(Error.Validation("The note is at most 2000 characters."));
        }

        item.MarkImplemented(request.Date ?? DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime), request.Note);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    public async Task<Result<ChangeRequestDto>> CancelAsync(Guid id, CancelChangeRequestRequest request, CancellationToken cancellationToken)
    {
        var item = await changes.GetAsync(id, cancellationToken);
        if (item is null)
        {
            return Result.Failure<ChangeRequestDto>(Error.NotFound("Change request not found."));
        }

        // A pending one belongs to its approvers until they decide; an implemented one has happened.
        if (item.Status is ChangeRequestStatus.PendingApproval or ChangeRequestStatus.Implemented or ChangeRequestStatus.Cancelled)
        {
            return Result.Failure<ChangeRequestDto>(Error.Conflict("This change request cannot be cancelled now."));
        }

        if (request.Note is { Length: > 2000 })
        {
            return Result.Failure<ChangeRequestDto>(Error.Validation("The note is at most 2000 characters."));
        }

        item.Cancel(request.Note);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    private static void Apply(ChangeRequest item, SaveChangeRequestRequest r) =>
        item.Update(r.Title, r.Description, r.Reason, r.Type, r.Priority, r.RequestedBy, r.ContractId, r.ScopeImpact, r.ScheduleImpactDays, r.CostImpact, r.OtherImpact);

    private async Task<Error?> ValidateAsync(Guid projectId, SaveChangeRequestRequest r, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Trim().Length > 300)
        {
            return Error.Validation("The title is required and at most 300 characters.");
        }

        if (!Enum.IsDefined(r.Type) || !Enum.IsDefined(r.Priority))
        {
            return Error.Validation("Unknown type or priority.");
        }

        if (new[] { r.Description, r.ScopeImpact }.Any(t => t is { Length: > 4000 })
            || new[] { r.Reason, r.OtherImpact }.Any(t => t is { Length: > 2000 })
            || r.RequestedBy is { Length: > 200 })
        {
            return Error.Validation("A text is longer than allowed.");
        }

        if (Math.Abs(r.CostImpact) > ContractMapper.MaxAmount)
        {
            return Error.Validation($"The cost impact must be within {ContractMapper.MaxAmount:N0} rials.");
        }

        if (r.ContractId is { } contractId && (await contracts.GetContractAsync(contractId, cancellationToken))?.ProjectId != projectId)
        {
            return Error.Validation("The contract is not one of this project's.");
        }

        return null;
    }

    public static ChangeRequestDto ToDto(ChangeRequest c) => new(
        c.Id, c.TenantId, c.ProjectId, c.Number, $"CR-{c.Number:000}", c.Title, c.Description, c.Reason, c.Type, c.Priority, c.RequestedBy, c.ContractId,
        c.ScopeImpact, c.ScheduleImpactDays, ContractMapper.Money(Math.Round(c.CostImpact, 0, MidpointRounding.AwayFromZero)), c.OtherImpact, c.HasImpactAssessment,
        c.Status, c.SubmittedAtUtc, c.DecidedAtUtc, c.DecisionNote, c.ImplementedDate, PersianDates.Format(c.ImplementedDate), c.OutcomeNote, c.CreatedAtUtc, c.CreatedByUserId);
}
