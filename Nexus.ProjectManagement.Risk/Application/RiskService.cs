using System.Text.Json;
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
    IApprovalRequester approvalRequester,
    IRiskSettingsRepository? settingsRepository = null) : IRiskService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> Colors = ["green", "yellow", "red"];

    public async Task<Result<IReadOnlyList<RiskDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var risks = await repository.ListByProjectAsync(projectId, cancellationToken);
        var settings = await SettingsForAsync([projectId], cancellationToken);
        return Result.Success<IReadOnlyList<RiskDto>>(risks.Select(risk => ToDto(risk, settings)).ToList());
    }

    public async Task<Result<IReadOnlyList<RiskDto>>> ListForProjectsAsync(ListRisksForProjectsRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ProjectIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<RiskDto>>([]);
        }

        var risks = await repository.ListByProjectsAsync(ids, cancellationToken);
        var settings = await SettingsForAsync(ids, cancellationToken);
        return Result.Success<IReadOnlyList<RiskDto>>(risks.Select(risk => ToDto(risk, settings)).ToList());
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
            : Result.Success(ToDto(risk, await SettingsForAsync([risk.ProjectId], cancellationToken)));
    }

    public async Task<Result<RiskDto>> CreateAsync(CreateRiskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<RiskDto>(Error.Validation("Description is required."));
        }

        var (probability, severity, impact) = Scores(request.ProbabilityScore, request.SeverityScore, request.ImpactScore,
            request.ProbabilityLevel, request.ImpactLevel);
        var risk = new Risk(Guid.NewGuid(), request.TenantId, request.ProjectId, request.Description, probability, severity, impact);
        risk.UpdateDetails(request.Description, probability, severity, impact, request.ResponsePlan, request.RiskOwnerUserId);

        await repository.AddAsync(risk, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(risk, await SettingsForAsync([risk.ProjectId], cancellationToken)));
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

        var (probability, severity, impact) = Scores(request.ProbabilityScore, request.SeverityScore, request.ImpactScore,
            request.ProbabilityLevel, request.ImpactLevel);
        risk.UpdateDetails(request.Description, probability, severity, impact, request.ResponsePlan, request.RiskOwnerUserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(risk, await SettingsForAsync([risk.ProjectId], cancellationToken)));
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
        return Result.Success(ToDto(risk, await SettingsForAsync([risk.ProjectId], cancellationToken)));
    }

    public async Task<Result<RiskDto>> ChangeResponseStatusAsync(Guid id, ChangeRiskResponseStatusRequest request, CancellationToken cancellationToken)
    {
        var risk = await repository.GetByIdAsync(id, cancellationToken);
        if (risk is null)
        {
            return Result.Failure<RiskDto>(Error.NotFound("Risk not found."));
        }

        if (!Enum.IsDefined(request.Status))
        {
            return Result.Failure<RiskDto>(Error.Validation("Choose Planning, InProgress, OnHold or Completed."));
        }

        if (risk.ApprovalStatus != ApprovalStatus.Approved)
        {
            return Result.Failure<RiskDto>(Error.Conflict("Only an approved risk has a response to report on."));
        }

        risk.ChangeResponseStatus(request.Status);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(risk, await SettingsForAsync([risk.ProjectId], cancellationToken)));
    }

    // --------------------------------------------------------------------------------------------- settings

    public async Task<Result<RiskSettingsDto>> GetSettingsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var all = await GetSettingsForProjectsAsync(new RiskSettingsForProjectsRequest([projectId]), cancellationToken);
        return all.IsFailure ? Result.Failure<RiskSettingsDto>(all.Error) : Result.Success(all.Value!.Single());
    }

    public async Task<Result<IReadOnlyList<RiskSettingsDto>>> GetSettingsForProjectsAsync(RiskSettingsForProjectsRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ProjectIds.Distinct().ToList();
        var stored = await SettingsForAsync(ids, cancellationToken);
        return Result.Success<IReadOnlyList<RiskSettingsDto>>(ids.Select(id => SettingsDtoOf(id, stored.GetValueOrDefault(id))).ToList());
    }

    public async Task<Result<RiskSettingsDto>> SaveSettingsAsync(SaveRiskSettingsRequest request, CancellationToken cancellationToken)
    {
        if (settingsRepository is null)
        {
            return Result.Failure<RiskSettingsDto>(Error.Conflict("Risk settings are not available in this deployment."));
        }

        if (request.Definitions is { Count: > 0 } definitions && DefinitionsError(definitions) is { } definitionsError)
        {
            return Result.Failure<RiskSettingsDto>(definitionsError);
        }

        if (request.MatrixColors is { Count: > 0 } colors && ColorsError(colors) is { } colorsError)
        {
            return Result.Failure<RiskSettingsDto>(colorsError);
        }

        var settings = await settingsRepository.GetAsync(request.ProjectId, cancellationToken);
        if (settings is null)
        {
            settings = new RiskProjectSettings(Guid.NewGuid(), request.TenantId, request.ProjectId);
            await settingsRepository.AddAsync(settings, cancellationToken);
        }

        if (request.Definitions is not null)
        {
            settings.SetDefinitions(request.Definitions.Count == 0 ? null : JsonSerializer.Serialize(request.Definitions, Json));
        }

        if (request.MatrixColors is not null)
        {
            settings.SetMatrixColors(request.MatrixColors.Count == 0 ? null : JsonSerializer.Serialize(request.MatrixColors, Json));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(SettingsDtoOf(request.ProjectId, settings));
    }

    private static Error? DefinitionsError(IReadOnlyList<RiskLevelDefinitionDto> definitions)
    {
        var wanted = new[] { "probability", "impact" }.SelectMany(axis => RiskLevels.All.Select(level => (axis, level))).ToHashSet();
        var given = definitions.Select(d => (d.Axis?.Trim().ToLowerInvariant() ?? string.Empty, d.Level?.Trim().ToLowerInvariant() ?? string.Empty)).ToList();
        return given.Count != wanted.Count || !wanted.SetEquals(given)
            ? Error.Validation("Give a definition for each of the six bands: probability and impact, each low, medium and high - once each.")
            : null;
    }

    private static Error? ColorsError(IReadOnlyList<RiskMatrixColorDto> colors)
    {
        var wanted = RiskLevels.All.SelectMany(p => RiskLevels.All.Select(i => (p, i))).ToHashSet();
        var given = colors.Select(c => (c.Probability?.Trim().ToLowerInvariant() ?? string.Empty, c.Impact?.Trim().ToLowerInvariant() ?? string.Empty)).ToList();
        if (given.Count != wanted.Count || !wanted.SetEquals(given))
        {
            return Error.Validation("Give a colour for each of the nine cells: probability x impact, each low, medium and high - once each.");
        }

        return colors.All(c => Colors.Contains(c.Color?.Trim().ToLowerInvariant() ?? string.Empty))
            ? null
            : Error.Validation("A cell's colour must be green, yellow or red.");
    }

    private async Task<Dictionary<Guid, RiskProjectSettings>> SettingsForAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        settingsRepository is null
            ? []
            : (await settingsRepository.ListAsync(projectIds, cancellationToken)).ToDictionary(s => s.ProjectId);

    private RiskSettingsDto SettingsDtoOf(Guid projectId, RiskProjectSettings? stored)
    {
        var definitions = Read<RiskLevelDefinitionDto>(stored?.DefinitionsJson)
            ?? RiskDefaults.Definitions.Select(d => new RiskLevelDefinitionDto(d.Axis, d.Level, d.Range, d.Description)).ToList();
        var colors = Read<RiskMatrixColorDto>(stored?.MatrixColorsJson)
            ?? RiskDefaults.MatrixColors.Select(c => new RiskMatrixColorDto(c.Probability, c.Impact, c.Color)).ToList();
        return new RiskSettingsDto(projectId, definitions, colors, stored?.DefinitionsJson is null && stored?.MatrixColorsJson is null);
    }

    private static List<T>? Read<T>(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<List<T>>(json, Json);

    private static string ColorOf(Dictionary<Guid, RiskProjectSettings> settings, Guid projectId, string probability, string impact)
    {
        var colors = Read<RiskMatrixColorDto>(settings.GetValueOrDefault(projectId)?.MatrixColorsJson)
            ?? RiskDefaults.MatrixColors.Select(c => new RiskMatrixColorDto(c.Probability, c.Impact, c.Color)).ToList();
        return colors.FirstOrDefault(c => c.Probability == probability && c.Impact == impact)?.Color ?? "yellow";
    }

    private static (int Probability, int Severity, int Impact) Scores(int probability, int severity, int impact, string? probabilityLevel, string? impactLevel)
    {
        var p = RiskLevels.ToScore(probabilityLevel) ?? probability;
        var i = RiskLevels.ToScore(impactLevel) ?? impact;
        // A band is given for impact only: severity follows it (there is no separate severity to ask about).
        var s = RiskLevels.ToScore(impactLevel) ?? severity;
        return (p, s, i);
    }

    private static RiskDto ToDto(Risk risk, Dictionary<Guid, RiskProjectSettings> settings)
    {
        var probabilityLevel = RiskLevels.FromScore(risk.ProbabilityScore);
        var impactLevel = RiskLevels.FromScore(risk.ImpactScore);
        return new RiskDto(
            risk.Id, risk.TenantId, risk.ProjectId, risk.Description,
            risk.ProbabilityScore, risk.SeverityScore, risk.ImpactScore, risk.Rpn,
            risk.ResponsePlan, risk.RiskOwnerUserId, risk.ApprovalStatus,
            risk.CreatedByUserId, risk.CreatedAtUtc,
            probabilityLevel, impactLevel, ColorOf(settings, risk.ProjectId, probabilityLevel, impactLevel), risk.ResponseStatus);
    }
}
