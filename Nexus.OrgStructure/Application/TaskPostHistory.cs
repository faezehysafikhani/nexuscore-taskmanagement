using NexusCore.Application.Identity.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// One stretch of time someone was on a task through one of its posts. Role: "creator",
/// "responsible" or "acting" (the stand-in, جانشین, of its vacant responsible post). PostTitle,
/// PostCode and Place: the task's post then; PositionTitle: the person's own position then (a
/// stand-in's is their own post's). EndedAtUtc null: still on it.
/// </summary>
public sealed record TaskPostPeriodDto(
    Guid TaskId,
    Guid UserId,
    string Role,
    Guid PostId,
    string PostTitle,
    string PostCode,
    string Place,
    Guid PositionId,
    string PositionTitle,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc);

/// <summary>
/// Who was on each task through its posts, and when (where tasks are given to posts; see
/// <see cref="TaskPostDirectory"/>). Empty where tasks are given to people.
/// </summary>
public interface ITaskPostHistory
{
    /// <summary>The periods of these tasks, oldest first.</summary>
    Task<IReadOnlyList<TaskPostPeriodDto>> ForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken);

    /// <summary>The periods of these people (null: anyone) or of whoever had this position at the time.</summary>
    Task<IReadOnlyList<TaskPostPeriodDto>> FindAsync(Guid tenantId, IReadOnlyCollection<Guid>? userIds, Guid? positionId, CancellationToken cancellationToken);
}

public sealed class TaskPostHistory(IOrgStructureRepository repository) : ITaskPostHistory
{
    public async Task<IReadOnlyList<TaskPostPeriodDto>> ForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        taskIds.Count == 0
            ? []
            : (await repository.ListTaskPostPeriodsAsync(tenantId, taskIds.Distinct().ToList(), openOnly: false, cancellationToken)).Select(ToDto).ToList();

    public async Task<IReadOnlyList<TaskPostPeriodDto>> FindAsync(Guid tenantId, IReadOnlyCollection<Guid>? userIds, Guid? positionId, CancellationToken cancellationToken) =>
        userIds is { Count: 0 }
            ? []
            : (await repository.FindTaskPostPeriodsAsync(tenantId, userIds?.Distinct().ToList(), positionId, cancellationToken)).Select(ToDto).ToList();

    private static TaskPostPeriodDto ToDto(Domain.TaskPostPeriod period) => new(
        period.TaskId,
        period.UserId,
        period.Role switch
        {
            (int)TaskPostRole.Owner => "creator",
            (int)TaskPostRole.Acting => "acting",
            _ => "responsible"
        },
        period.PostId,
        period.PostTitle,
        period.PostCode,
        period.Place,
        period.PositionId,
        period.PositionTitle,
        period.StartedAtUtc,
        period.EndedAtUtc);
}
