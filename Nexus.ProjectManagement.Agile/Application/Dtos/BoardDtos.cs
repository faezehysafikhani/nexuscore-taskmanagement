using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Agile.Application.Dtos;

/// <summary>A task as shown on a Kanban card, with the counts of its checklist.</summary>
public sealed record BoardCardDto(
    Guid Id, string Title, AgileTaskStatus Status, AgileTaskPriority Priority, Guid? ResponsibleUserId,
    DateOnly? DueDate, int? SprintNumber, int? StoryPoints, int Rank,
    int ChecklistDone, int ChecklistTotal, ApprovalStatus ApprovalStatus);

public sealed record BoardColumnDto(AgileTaskStatus Status, int CardCount, int Points, IReadOnlyList<BoardCardDto> Cards);

/// <summary>The four columns in board order: ToDo, InProgress, UnderReview, Done. SprintNumber is
/// null for the whole project's board.</summary>
public sealed record BoardDto(Guid ProjectId, int? SprintNumber, IReadOnlyList<BoardColumnDto> Columns);

/// <summary>Puts a task in a column, in front of BeforeTaskId (a card already in that column), or at the end when null.</summary>
public sealed record MoveAgileTaskRequest(AgileTaskStatus Status, Guid? BeforeTaskId);

/// <summary>An explicit null clears the estimate (back to "not estimated").</summary>
public sealed record SetStoryPointsRequest(int? StoryPoints);

public sealed record AgileChecklistItemDto(Guid Id, Guid TaskId, string Text, bool IsDone, int Order);

public sealed record CreateChecklistItemRequest(string Text);

public sealed record UpdateChecklistItemRequest(string Text, bool IsDone);

/// <summary>The tasks that are in no sprint and not yet done, in board order; UnestimatedCount is how many have no story points.</summary>
public sealed record BacklogDto(Guid ProjectId, IReadOnlyList<BoardCardDto> Cards, int TotalPoints, int UnestimatedCount);
