using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public sealed class WaterfallSettingsService(
    IWaterfallSettingsRepository repository,
    IWaterfallUnitOfWork unitOfWork,
    WaterfallOptions? options = null) : IWaterfallSettingsService
{
    public const int DefaultTimeFactor = 34;
    public const int DefaultCostFactor = 33;
    public const int DefaultComplexityFactor = 33;

    public async Task<Result<WaterfallSettingsDto>> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var all = await GetForProjectsAsync(new WaterfallSettingsForProjectsRequest([projectId]), cancellationToken);
        return all.IsFailure ? Result.Failure<WaterfallSettingsDto>(all.Error) : Result.Success(all.Value!.Single());
    }

    public async Task<Result<IReadOnlyList<WaterfallSettingsDto>>> GetForProjectsAsync(
        WaterfallSettingsForProjectsRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ProjectIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<WaterfallSettingsDto>>([]);
        }

        var stored = (await repository.ListAsync(ids, cancellationToken)).ToDictionary(s => s.ProjectId);
        return Result.Success<IReadOnlyList<WaterfallSettingsDto>>(ids.Select(id => ToDto(id, stored.GetValueOrDefault(id), options)).ToList());
    }

    public async Task<Result<WaterfallSettingsDto>> SaveAsync(SaveWaterfallSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Weighting) || !Enum.IsDefined(request.PlannedProgress))
        {
            return Result.Failure<WaterfallSettingsDto>(Error.Validation("Unknown weighting or planned progress mode."));
        }

        if (new[] { request.TimeFactor, request.CostFactor, request.ComplexityFactor }.Any(f => f is < 0 or > 100))
        {
            return Result.Failure<WaterfallSettingsDto>(Error.Validation("Each factor must be between 0 and 100."));
        }

        if (request.Weighting == WeightingMode.Combined && request.TimeFactor + request.CostFactor + request.ComplexityFactor != 100)
        {
            return Result.Failure<WaterfallSettingsDto>(Error.Validation("The time, cost and complexity factors must add up to 100."));
        }

        var settings = await repository.GetAsync(request.ProjectId, cancellationToken);
        if (settings is null)
        {
            settings = new WaterfallProjectSettings(Guid.NewGuid(), request.TenantId, request.ProjectId);
            await repository.AddAsync(settings, cancellationToken);
        }

        settings.Set(request.Weighting, request.TimeFactor, request.CostFactor, request.ComplexityFactor, request.PlannedProgress);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(request.ProjectId, settings, options));
    }

    /// <summary>The settings a project effectively has: its own, or the defaults.</summary>
    public static WaterfallSettingsDto ToDto(Guid projectId, WaterfallProjectSettings? stored, WaterfallOptions? options) =>
        stored is null
            ? new WaterfallSettingsDto(
                projectId, WeightingMode.Manual, DefaultTimeFactor, DefaultCostFactor, DefaultComplexityFactor,
                options?.DefaultPlannedProgress ?? PlannedProgressMode.Stored, IsDefault: true)
            : new WaterfallSettingsDto(
                projectId, stored.Weighting, stored.TimeFactor, stored.CostFactor, stored.ComplexityFactor, stored.PlannedProgress, IsDefault: false);
}
