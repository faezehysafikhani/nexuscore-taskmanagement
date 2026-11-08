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
    IApprovalRequester approvalRequester,
    IStakeholderSettingsRepository? settingsRepository = null) : IStakeholderService
{
    public async Task<Result<IReadOnlyList<StakeholderDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var stakeholders = await repository.ListByProjectAsync(projectId, cancellationToken);
        var settings = await SettingsForAsync([projectId], cancellationToken);
        return Result.Success<IReadOnlyList<StakeholderDto>>(stakeholders.Select(s => ToDto(s, settings)).ToList());
    }

    public async Task<Result<IReadOnlyList<StakeholderDto>>> ListForProjectsAsync(ListStakeholdersForProjectsRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ProjectIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<StakeholderDto>>([]);
        }

        var stakeholders = await repository.ListByProjectsAsync(ids, cancellationToken);
        var settings = await SettingsForAsync(ids, cancellationToken);
        return Result.Success<IReadOnlyList<StakeholderDto>>(stakeholders.Select(s => ToDto(s, settings)).ToList());
    }

    // --------------------------------------------------------------------------------------------- settings

    public async Task<Result<StakeholderSettingsDto>> GetSettingsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var all = await GetSettingsForProjectsAsync(new StakeholderSettingsForProjectsRequest([projectId]), cancellationToken);
        return all.IsFailure ? Result.Failure<StakeholderSettingsDto>(all.Error) : Result.Success(all.Value!.Single());
    }

    public async Task<Result<IReadOnlyList<StakeholderSettingsDto>>> GetSettingsForProjectsAsync(StakeholderSettingsForProjectsRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ProjectIds.Distinct().ToList();
        var stored = await SettingsForAsync(ids, cancellationToken);
        return Result.Success<IReadOnlyList<StakeholderSettingsDto>>(ids.Select(id => SettingsDtoOf(id, stored.GetValueOrDefault(id))).ToList());
    }

    public async Task<Result<StakeholderSettingsDto>> SaveSettingsAsync(SaveStakeholderSettingsRequest request, CancellationToken cancellationToken)
    {
        if (settingsRepository is null)
        {
            return Result.Failure<StakeholderSettingsDto>(Error.Conflict("Stakeholder settings are not available in this deployment."));
        }

        var settings = await settingsRepository.GetAsync(request.ProjectId, cancellationToken);
        if (settings is null)
        {
            settings = new StakeholderProjectSettings(Guid.NewGuid(), request.TenantId, request.ProjectId);
            await settingsRepository.AddAsync(settings, cancellationToken);
        }

        settings.SetStrategies(request.ManageClosely, request.KeepSatisfied, request.KeepInformed, request.Monitor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(SettingsDtoOf(request.ProjectId, settings));
    }

    private async Task<Dictionary<Guid, StakeholderProjectSettings>> SettingsForAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        settingsRepository is null
            ? []
            : (await settingsRepository.ListAsync(projectIds, cancellationToken)).ToDictionary(s => s.ProjectId);

    private static StakeholderSettingsDto SettingsDtoOf(Guid projectId, StakeholderProjectSettings? stored) => new(
        projectId,
        stored?.ManageClosely ?? StakeholderStrategyDefaults.ManageClosely,
        stored?.KeepSatisfied ?? StakeholderStrategyDefaults.KeepSatisfied,
        stored?.KeepInformed ?? StakeholderStrategyDefaults.KeepInformed,
        stored?.Monitor ?? StakeholderStrategyDefaults.Monitor,
        stored is null || (stored.ManageClosely is null && stored.KeepSatisfied is null && stored.KeepInformed is null && stored.Monitor is null));

    /// <summary>What to do about a stakeholder: what was written for them, else the project's wording (or the default) for their quadrant.</summary>
    private static string StrategyOf(Stakeholder stakeholder, Dictionary<Guid, StakeholderProjectSettings> settings)
    {
        if (!string.IsNullOrWhiteSpace(stakeholder.EngagementStrategy))
        {
            return stakeholder.EngagementStrategy;
        }

        var wording = SettingsDtoOf(stakeholder.ProjectId, settings.GetValueOrDefault(stakeholder.ProjectId));
        return ToQuadrant(stakeholder.Power, stakeholder.Interest) switch
        {
            StakeholderQuadrant.ManageClosely => wording.ManageClosely,
            StakeholderQuadrant.KeepSatisfied => wording.KeepSatisfied,
            StakeholderQuadrant.KeepInformed => wording.KeepInformed,
            _ => wording.Monitor
        };
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
            : Result.Success(ToDto(stakeholder, await SettingsForAsync([stakeholder.ProjectId], cancellationToken)));
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
        return Result.Success(ToDto(stakeholder, await SettingsForAsync([stakeholder.ProjectId], cancellationToken)));
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
        return Result.Success(ToDto(stakeholder, await SettingsForAsync([stakeholder.ProjectId], cancellationToken)));
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
        return Result.Success(ToDto(stakeholder, await SettingsForAsync([stakeholder.ProjectId], cancellationToken)));
    }

    private static StakeholderDto ToDto(Stakeholder stakeholder, Dictionary<Guid, StakeholderProjectSettings> settings) => new(
        stakeholder.Id, stakeholder.TenantId, stakeholder.ProjectId, stakeholder.Name, stakeholder.IsInternal,
        stakeholder.Expectations, stakeholder.Notes, stakeholder.Power, stakeholder.Interest,
        stakeholder.EngagementStrategy, stakeholder.Requirements, stakeholder.ApprovalStatus, stakeholder.CreatedByUserId,
        ToQuadrant(stakeholder.Power, stakeholder.Interest), StrategyOf(stakeholder, settings));
}
