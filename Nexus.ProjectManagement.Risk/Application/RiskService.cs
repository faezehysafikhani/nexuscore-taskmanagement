using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using Nexus.ProjectManagement.RiskManagement.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.RiskManagement.Application;

/// <summary>
/// Fully usable with neither Workflow nor an optional analyzer installed: SubmitForApprovalAsync falls back to
/// direct-approve when IApprovalRequester reports NotConfigured (the NullApprovalRequester
/// default registered by NexusCore when Workflow isn't part of the composition).
/// </summary>
public sealed class RiskService(
    IRiskRepository repository,
    IRiskUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IRiskService
{
    public async Task<Result<IReadOnlyList<RiskDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var risks = await repository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<RiskDto>>(risks.Select(ToDto).ToList());
    }

    public async Task<Result<RiskMatrixDto>> GetMatrixAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var risks = await repository.ListByProjectAsync(projectId, cancellationToken);

        var cells = risks
            .GroupBy(risk => (risk.ProbabilityScore, risk.ImpactScore))
            .OrderBy(group => group.Key.ProbabilityScore)
            .ThenBy(group => group.Key.ImpactScore)
            .Select(group => new RiskMatrixCellDto(
                group.Key.ProbabilityScore, group.Key.ImpactScore, group.Count(), group.Select(risk => risk.Id).ToList()))
            .ToList();

        return Result.Success(new RiskMatrixDto(projectId, risks.Count, cells));
    }

    public async Task<Result<RiskDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var risk = await repository.GetByIdAsync(id, cancellationToken);
        return risk is null
            ? Result.Failure<RiskDto>(Error.NotFound("Risk not found."))
            : Result.Success(ToDto(risk));
    }

    public async Task<Result<RiskDto>> CreateAsync(CreateRiskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<RiskDto>(Error.Validation("Description is required."));
        }

        var risk = new Risk(Guid.NewGuid(), request.TenantId, request.ProjectId, request.Description,
            request.ProbabilityScore, request.SeverityScore, request.ImpactScore);
        risk.UpdateDetails(request.Description, request.ProbabilityScore, request.SeverityScore, request.ImpactScore,
            request.ResponsePlan, request.RiskOwnerUserId);

        await repository.AddAsync(risk, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(risk));
    }

    public async Task<Result<RiskDto>> UpdateAsync(Guid id, UpdateRiskRequest request, CancellationToken cancellationToken)
    {
        var risk = await repository.GetByIdAsync(id, cancellationToken);
        if (risk is null)
        {
            return Result.Failure<RiskDto>(Error.NotFound("Risk not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<RiskDto>(Error.Validation("Description is required."));
        }

        risk.UpdateDetails(request.Description, request.ProbabilityScore, request.SeverityScore, request.ImpactScore,
            request.ResponsePlan, request.RiskOwnerUserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(risk));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var risk = await repository.GetByIdAsync(id, cancellationToken);
        if (risk is null)
        {
            return Result.Failure(Error.NotFound("Risk not found."));
        }

        // An approval request is already open for this risk; deleting it now would leave the
        // workflow instance pointing at a subject that no longer exists.
        if (risk.ApprovalStatus == ApprovalStatus.PendingApproval)
        {
            return Result.Failure(Error.Conflict("A risk that is pending approval cannot be deleted."));
        }

        await repository.RemoveAsync(risk, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<RiskDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var risk = await repository.GetByIdAsync(id, cancellationToken);
        if (risk is null)
        {
            return Result.Failure<RiskDto>(Error.NotFound("Risk not found."));
        }

        var subject = new ApprovalSubject("Risk", risk.Id, risk.TenantId, ScopeType: "Project", ScopeId: risk.ProjectId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            risk.MarkPendingApproval();
        }
        else
        {
            // No Workflow installed: apply the direct-approve business rule immediately.
            risk.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(risk));
    }

    private static RiskDto ToDto(Risk risk) => new(
        risk.Id, risk.TenantId, risk.ProjectId, risk.Description,
        risk.ProbabilityScore, risk.SeverityScore, risk.ImpactScore, risk.Rpn,
        risk.ResponsePlan, risk.RiskOwnerUserId, risk.ApprovalStatus,
        risk.CreatedByUserId, risk.CreatedAtUtc);
}
