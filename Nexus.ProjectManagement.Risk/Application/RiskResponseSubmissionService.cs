using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using Nexus.ProjectManagement.RiskManagement.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.RiskManagement.Application;

/// <summary>
/// Reporting where the work on a risk's response stands and having someone verify it. Works with or without an approval backend: with none
/// the risk takes the status at once, like every other optional approval in the project-management modules.
/// </summary>
public sealed class RiskResponseSubmissionService(
    IRiskResponseSubmissionRepository repository,
    IRiskRepository riskRepository,
    IRiskService riskService,
    IRiskUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IRiskResponseSubmissionService
{
    public const string SubjectType = "RiskResponseStatus";

    public async Task<Result<IReadOnlyList<RiskResponseSubmissionDto>>> ListForProjectsAsync(ListRisksForProjectsRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ProjectIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<RiskResponseSubmissionDto>>([]);
        }

        var items = await repository.ListByProjectsAsync(ids, cancellationToken);
        return Result.Success<IReadOnlyList<RiskResponseSubmissionDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<RiskResponseSubmissionDto>>> ListByRiskAsync(Guid riskId, CancellationToken cancellationToken)
    {
        var items = await repository.ListByRiskAsync(riskId, cancellationToken);
        return Result.Success<IReadOnlyList<RiskResponseSubmissionDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<RiskResponseSubmissionDto>> SubmitAsync(SubmitRiskResponseRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Status))
        {
            return Result.Failure<RiskResponseSubmissionDto>(Error.Validation("Choose Planning, InProgress, OnHold or Completed."));
        }

        var risk = await riskRepository.GetByIdAsync(request.RiskId, cancellationToken);
        if (risk is null)
        {
            return Result.Failure<RiskResponseSubmissionDto>(Error.NotFound("Risk not found."));
        }

        if (risk.ApprovalStatus != ApprovalStatus.Approved)
        {
            return Result.Failure<RiskResponseSubmissionDto>(Error.Conflict("Only an approved risk has a response to report on."));
        }

        if (risk.ResponseStatus == request.Status)
        {
            return Result.Failure<RiskResponseSubmissionDto>(Error.Validation("The response already has that status."));
        }

        var earlier = await repository.ListByRiskAsync(risk.Id, cancellationToken);
        if (earlier.Any(s => s.ApprovalStatus == ApprovalStatus.PendingApproval))
        {
            return Result.Failure<RiskResponseSubmissionDto>(Error.Conflict("An earlier report for this risk is still waiting for verification."));
        }

        var submission = new RiskResponseSubmission(
            Guid.NewGuid(), risk.TenantId, risk.ProjectId, risk.Id, risk.ResponseStatus, request.Status, request.Comment);
        await repository.AddAsync(submission, cancellationToken);

        var subject = new ApprovalSubject(SubjectType, submission.Id, submission.TenantId, ScopeType: "Project", ScopeId: submission.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);
        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            submission.MarkPendingApproval();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(submission));
        }

        submission.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var moved = await riskService.ChangeResponseStatusAsync(risk.Id, new ChangeRiskResponseStatusRequest(request.Status), cancellationToken);
        return moved.IsFailure ? Result.Failure<RiskResponseSubmissionDto>(moved.Error) : Result.Success(ToDto(submission));
    }

    private static RiskResponseSubmissionDto ToDto(RiskResponseSubmission s) => new(
        s.Id, s.TenantId, s.ProjectId, s.RiskId, s.PreviousStatus, s.SubmittedStatus, s.Comment, s.ApprovalStatus,
        s.CreatedByUserId, s.CreatedAtUtc);
}
