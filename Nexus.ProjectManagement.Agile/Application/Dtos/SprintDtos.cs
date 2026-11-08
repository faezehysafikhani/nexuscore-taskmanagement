using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Application.Dtos;

/// <summary>TaskCount, TotalPoints and DonePoints describe the tasks in the sprint right now.</summary>
public sealed record SprintDto(
    Guid Id, Guid TenantId, Guid ProjectId, int Number, string Name, string? Goal,
    DateOnly? StartDate, DateOnly? EndDate, SprintStatus Status,
    int TaskCount, int TotalPoints, int DonePoints);

/// <summary>Name defaults to "Sprint N". Dates may be left out until the sprint is started.</summary>
public sealed record CreateSprintRequest(Guid TenantId, Guid ProjectId, string? Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate);

public sealed record UpdateSprintRequest(string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Dates given here replace the sprint's own; either way both must be known to start.</summary>
public sealed record StartSprintRequest(DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Where unfinished tasks go when the sprint ends: another sprint of the project, or the backlog when null.</summary>
public sealed record CompleteSprintRequest(Guid? MoveIncompleteToSprintId);

public sealed record CompleteSprintResultDto(SprintDto Sprint, int CompletedTasks, int CarriedOverTasks);

public sealed record AssignSprintTasksRequest(IReadOnlyList<Guid> TaskIds);
