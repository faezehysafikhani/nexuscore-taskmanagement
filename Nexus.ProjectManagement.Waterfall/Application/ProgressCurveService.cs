using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public sealed class ProgressCurveService(
    IProgressSnapshotRepository repository,
    IScheduleService scheduleService,
    IWaterfallUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IProgressCurveService
{
    public const int DefaultStepDays = 7;
    public const int MaxStepDays = 90;

    /// <summary>Longest series returned; a wider range is sampled more coarsely instead.</summary>
    public const int MaxPlannedPoints = 1000;

    public async Task<Result<IReadOnlyList<ProgressSnapshotDto>>> ListSnapshotsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var snapshots = await repository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<ProgressSnapshotDto>>(snapshots.OrderBy(s => s.SnapshotDate).Select(ToDto).ToList());
    }

    public async Task<Result<ProgressSnapshotDto>> CreateSnapshotAsync(CreateProgressSnapshotRequest request, CancellationToken cancellationToken)
    {
        var calculation = await scheduleService.CalculateAsync(request.ProjectId, cancellationToken);
        if (calculation.IsFailure)
        {
            return Result.Failure<ProgressSnapshotDto>(calculation.Error);
        }

        var date = request.SnapshotDate ?? Today();
        var result = calculation.Value!.Result;
        var planned = Round(new PlannedProgressCurve(result).At(date));

        var existing = await repository.GetByDateAsync(request.ProjectId, date, cancellationToken);
        if (existing is not null)
        {
            existing.Refresh(planned, result.ActualProgress, request.Note);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(existing));
        }

        var snapshot = new ProgressSnapshot(Guid.NewGuid(), request.TenantId, request.ProjectId, date, planned, result.ActualProgress, request.Note);
        await repository.AddAsync(snapshot, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(snapshot));
    }

    public async Task<Result> DeleteSnapshotAsync(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await repository.GetByIdAsync(id, cancellationToken);
        if (snapshot is null)
        {
            return Result.Failure(Error.NotFound("Progress snapshot not found."));
        }

        await repository.RemoveAsync(snapshot, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<SCurveDto>> GetSCurveAsync(Guid projectId, int stepDays, CancellationToken cancellationToken)
    {
        if (stepDays < 1 || stepDays > MaxStepDays)
        {
            return Result.Failure<SCurveDto>(Error.Validation($"stepDays must be between 1 and {MaxStepDays}."));
        }

        var calculation = await scheduleService.CalculateAsync(projectId, cancellationToken);
        if (calculation.IsFailure)
        {
            return Result.Failure<SCurveDto>(calculation.Error);
        }

        var result = calculation.Value!.Result;
        var curve = new PlannedProgressCurve(result);

        var span = result.ProjectFinish.DayNumber - result.ProjectStart.DayNumber;
        var step = Math.Max(stepDays, (int)Math.Ceiling(span / (double)MaxPlannedPoints));

        var planned = new List<CurvePointDto>();
        for (var date = result.ProjectStart; date < result.ProjectFinish; date = date.AddDays(step))
        {
            planned.Add(new CurvePointDto(date, Round(curve.At(date))));
        }

        planned.Add(new CurvePointDto(result.ProjectFinish, Round(curve.At(result.ProjectFinish))));

        var snapshots = await repository.ListByProjectAsync(projectId, cancellationToken);
        var actual = snapshots.OrderBy(s => s.SnapshotDate)
            .Select(s => new ActualPointDto(s.SnapshotDate, s.ActualProgress, s.PlannedProgress))
            .ToList();

        var today = Today();
        var currentPlanned = Round(curve.At(today));
        var currentActual = result.ActualProgress;
        return Result.Success(new SCurveDto(
            projectId, result.ProjectStart, result.ProjectFinish, step, today, planned, actual,
            currentPlanned, currentActual, Round(currentActual - currentPlanned),
            currentPlanned > 0 ? Math.Round(currentActual / currentPlanned, 2) : null));
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static decimal Round(decimal value) => Math.Round(value, 2);

    private static ProgressSnapshotDto ToDto(ProgressSnapshot s) => new(
        s.Id, s.TenantId, s.ProjectId, s.SnapshotDate, s.PlannedProgress, s.ActualProgress, s.Note, s.CreatedByUserId);
}
