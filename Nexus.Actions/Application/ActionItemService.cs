using Nexus.Actions.Application.Dtos;
using Nexus.Actions.Domain;
using Nexus.Calendar.Application;
using Nexus.Organization.Application;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Actions.Application;

/// <summary>
/// Organization and Calendar are required (hard project references) - CreateAsync/UpdateAsync
/// validate the given ids actually exist. ProjectManagement.Core is optional and never
/// referenced: ProjectId is accepted and stored as-is with no existence check, since this
/// module must build and run whether or not that module is installed.
/// </summary>
public sealed class ActionItemService(
    IActionItemRepository repository,
    IOrganizationUnitRepository organizationUnitRepository,
    IWorkCalendarRepository workCalendarRepository,
    IActionsUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IActionItemService
{
    public async Task<Result<IReadOnlyList<ActionItemDto>>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken)
    {
        var actions = await repository.ListAsync(tenantId, projectId, cancellationToken);
        return Result.Success<IReadOnlyList<ActionItemDto>>(actions.Select(ToDto).ToList());
    }

    public async Task<Result<ActionItemDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var action = await repository.GetByIdAsync(id, cancellationToken);
        return action is null
            ? Result.Failure<ActionItemDto>(Error.NotFound("Action not found."))
            : Result.Success(ToDto(action));
    }

    public async Task<Result<ActionItemDto>> CreateAsync(CreateActionItemRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<ActionItemDto>(Error.Validation("Title is required."));
        }

        var referenceError = await ValidateRequiredReferencesAsync(request.OrganizationUnitId, request.WorkCalendarId, cancellationToken);
        if (referenceError is not null)
        {
            return Result.Failure<ActionItemDto>(referenceError);
        }

        var action = new ActionItem(Guid.NewGuid(), request.TenantId, request.Title, request.OrganizationUnitId, request.WorkCalendarId, request.ProjectId);
        action.UpdateDetails(
            request.Title, request.Description, request.OwnerUserId, request.ResponsibleUserId,
            request.OrganizationUnitId, request.WorkCalendarId, request.ProjectId, request.StartDate, request.EndDate);

        await repository.AddAsync(action, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(action));
    }

    public async Task<Result<ActionItemDto>> UpdateAsync(Guid id, UpdateActionItemRequest request, CancellationToken cancellationToken)
    {
        var action = await repository.GetByIdAsync(id, cancellationToken);
        if (action is null)
        {
            return Result.Failure<ActionItemDto>(Error.NotFound("Action not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<ActionItemDto>(Error.Validation("Title is required."));
        }

        var referenceError = await ValidateRequiredReferencesAsync(request.OrganizationUnitId, request.WorkCalendarId, cancellationToken);
        if (referenceError is not null)
        {
            return Result.Failure<ActionItemDto>(referenceError);
        }

        action.UpdateDetails(
            request.Title, request.Description, request.OwnerUserId, request.ResponsibleUserId,
            request.OrganizationUnitId, request.WorkCalendarId, request.ProjectId, request.StartDate, request.EndDate);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(action));
    }

    public async Task<Result<ActionItemDto>> ChangeStatusAsync(Guid id, ChangeActionStatusRequest request, CancellationToken cancellationToken)
    {
        var action = await repository.GetByIdAsync(id, cancellationToken);
        if (action is null)
        {
            return Result.Failure<ActionItemDto>(Error.NotFound("Action not found."));
        }

        action.ChangeStatus(request.Status);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(action));
    }

    public async Task<Result<ActionItemDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var action = await repository.GetByIdAsync(id, cancellationToken);
        if (action is null)
        {
            return Result.Failure<ActionItemDto>(Error.NotFound("Action not found."));
        }

        var subject = new ApprovalSubject("Action", action.Id, action.TenantId,
            ScopeType: action.ProjectId is null ? null : "Project", ScopeId: action.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            action.MarkPendingApproval();
        }
        else
        {
            action.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(action));
    }

    private async Task<Error?> ValidateRequiredReferencesAsync(Guid organizationUnitId, Guid workCalendarId, CancellationToken cancellationToken)
    {
        if (await organizationUnitRepository.GetByIdAsync(organizationUnitId, cancellationToken) is null)
        {
            return Error.Validation("Organization unit was not found.");
        }

        if (await workCalendarRepository.GetByIdAsync(workCalendarId, cancellationToken) is null)
        {
            return Error.Validation("Work calendar was not found.");
        }

        return null;
    }

    private static ActionItemDto ToDto(ActionItem action) => new(
        action.Id, action.TenantId, action.Title, action.Description,
        action.OwnerUserId, action.ResponsibleUserId, action.Status,
        action.OrganizationUnitId, action.WorkCalendarId, action.ProjectId,
        action.StartDate, action.EndDate, action.ApprovalStatus);
}
