using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Progress.Application;

/// <summary>Fully usable without AI (rule: Progress Management باید بدون AI کاملاً قابل
/// استفاده باشد) and without Workflow, same optional-approval pattern as Risk/Stakeholder.</summary>
public sealed class ProgressService(
    IProgressRepository repository,
    IProgressUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IProgressService
{
    public async Task<Result<IReadOnlyList<ProgressUpdateDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var updates = await repository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<ProgressUpdateDto>>(updates.Select(ToDto).ToList());
    }

    public async Task<Result<ProgressUpdateDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var update = await repository.GetByIdAsync(id, cancellationToken);
        return update is null
            ? Result.Failure<ProgressUpdateDto>(Error.NotFound("Progress update not found."))
            : Result.Success(ToDto(update));
    }

    public async Task<Result<ProgressUpdateDto>> CreateAsync(CreateProgressUpdateRequest request, CancellationToken cancellationToken)
    {
        var update = new ProgressUpdate(Guid.NewGuid(), request.TenantId, request.ProjectId, request.RegisterDate, request.PlannedProgress, request.ActualProgress);
        update.UpdateDetails(request.StatusDescription, request.PlannedProgress, request.ActualProgress, request.DelayReasons);

        await repository.AddAsync(update, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(update));
    }

    public async Task<Result<ProgressUpdateDto>> UpdateAsync(Guid id, UpdateProgressUpdateRequest request, CancellationToken cancellationToken)
    {
        var update = await repository.GetByIdAsync(id, cancellationToken);
        if (update is null)
        {
            return Result.Failure<ProgressUpdateDto>(Error.NotFound("Progress update not found."));
        }

        update.UpdateDetails(request.StatusDescription, request.PlannedProgress, request.ActualProgress, request.DelayReasons);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(update));
    }

    public async Task<Result<ProgressUpdateDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var update = await repository.GetByIdAsync(id, cancellationToken);
        if (update is null)
        {
            return Result.Failure<ProgressUpdateDto>(Error.NotFound("Progress update not found."));
        }

        var subject = new ApprovalSubject("ProgressUpdate", update.Id, update.TenantId, ScopeType: "Project", ScopeId: update.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            update.MarkPendingApproval();
        }
        else
        {
            update.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(update));
    }

    private static ProgressUpdateDto ToDto(ProgressUpdate update) => new(
        update.Id, update.TenantId, update.ProjectId, update.StatusDescription, update.RegisterDate,
        update.PlannedProgress, update.ActualProgress, update.ConfirmedProgress, update.DelayReasons,
        update.Deviation, update.PerformanceClassification, update.ApprovalStatus, update.CreatedByUserId);
}
