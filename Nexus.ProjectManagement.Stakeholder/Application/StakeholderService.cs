using Nexus.ProjectManagement.StakeholderManagement.Application.Dtos;
using Nexus.ProjectManagement.StakeholderManagement.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.StakeholderManagement.Application;

/// <summary>Fully usable with neither Workflow nor an optional analyzer installed - same pattern as
/// Nexus.ProjectManagement.RiskManagement.Application.RiskService.</summary>
public sealed class StakeholderService(
    IStakeholderRepository repository,
    IStakeholderUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IStakeholderService
{
    public async Task<Result<IReadOnlyList<StakeholderDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var stakeholders = await repository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<StakeholderDto>>(stakeholders.Select(ToDto).ToList());
    }

    public async Task<Result<StakeholderMatrixDto>> GetMatrixAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var stakeholders = await repository.ListByProjectAsync(projectId, cancellationToken);

        var cells = stakeholders
            .GroupBy(stakeholder => (stakeholder.Power, stakeholder.Interest))
            .OrderBy(group => group.Key.Power)
            .ThenBy(group => group.Key.Interest)
            .Select(group => new StakeholderMatrixCellDto(
                group.Key.Power, group.Key.Interest, ToQuadrant(group.Key.Power, group.Key.Interest),
                group.Select(stakeholder => new StakeholderMatrixItemDto(stakeholder.Id, stakeholder.Name, stakeholder.IsInternal)).ToList()))
            .ToList();

        return Result.Success(new StakeholderMatrixDto(projectId, stakeholders.Count, cells));
    }

    private static StakeholderQuadrant ToQuadrant(PowerLevel power, InterestLevel interest) =>
        (power == PowerLevel.High, interest == InterestLevel.High) switch
        {
            (true, true) => StakeholderQuadrant.ManageClosely,
            (true, false) => StakeholderQuadrant.KeepSatisfied,
            (false, true) => StakeholderQuadrant.KeepInformed,
            _ => StakeholderQuadrant.Monitor
        };

    public async Task<Result<StakeholderDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var stakeholder = await repository.GetByIdAsync(id, cancellationToken);
        return stakeholder is null
            ? Result.Failure<StakeholderDto>(Error.NotFound("Stakeholder not found."))
            : Result.Success(ToDto(stakeholder));
    }

    public async Task<Result<StakeholderDto>> CreateAsync(CreateStakeholderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<StakeholderDto>(Error.Validation("Name is required."));
        }

        var stakeholder = new Stakeholder(Guid.NewGuid(), request.TenantId, request.ProjectId, request.Name, request.IsInternal);
        stakeholder.UpdateDetails(request.Name, request.IsInternal, request.Expectations, request.Notes,
            request.Power, request.Interest, request.EngagementStrategy, request.Requirements);

        await repository.AddAsync(stakeholder, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(stakeholder));
    }

    public async Task<Result<StakeholderDto>> UpdateAsync(Guid id, UpdateStakeholderRequest request, CancellationToken cancellationToken)
    {
        var stakeholder = await repository.GetByIdAsync(id, cancellationToken);
        if (stakeholder is null)
        {
            return Result.Failure<StakeholderDto>(Error.NotFound("Stakeholder not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<StakeholderDto>(Error.Validation("Name is required."));
        }

        stakeholder.UpdateDetails(request.Name, request.IsInternal, request.Expectations, request.Notes,
            request.Power, request.Interest, request.EngagementStrategy, request.Requirements);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(stakeholder));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var stakeholder = await repository.GetByIdAsync(id, cancellationToken);
        if (stakeholder is null)
        {
            return Result.Failure(Error.NotFound("Stakeholder not found."));
        }

        // An approval request is already open for this stakeholder; deleting it now would leave the
        // workflow instance pointing at a subject that no longer exists.
        if (stakeholder.ApprovalStatus == ApprovalStatus.PendingApproval)
        {
            return Result.Failure(Error.Conflict("A stakeholder that is pending approval cannot be deleted."));
        }

        await repository.RemoveAsync(stakeholder, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<StakeholderDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var stakeholder = await repository.GetByIdAsync(id, cancellationToken);
        if (stakeholder is null)
        {
            return Result.Failure<StakeholderDto>(Error.NotFound("Stakeholder not found."));
        }

        var subject = new ApprovalSubject("Stakeholder", stakeholder.Id, stakeholder.TenantId, ScopeType: "Project", ScopeId: stakeholder.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            stakeholder.MarkPendingApproval();
        }
        else
        {
            stakeholder.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(stakeholder));
    }

    private static StakeholderDto ToDto(Stakeholder stakeholder) => new(
        stakeholder.Id, stakeholder.TenantId, stakeholder.ProjectId, stakeholder.Name, stakeholder.IsInternal,
        stakeholder.Expectations, stakeholder.Notes, stakeholder.Power, stakeholder.Interest,
        stakeholder.EngagementStrategy, stakeholder.Requirements, stakeholder.ApprovalStatus, stakeholder.CreatedByUserId);
}
