using Nexus.ProjectManagement.History.Domain;

namespace Nexus.ProjectManagement.History.Application.Dtos;

public sealed record ProjectChangePropertyDto(string Property, string? OldValue, string? NewValue);

public sealed record ProjectChangeDto(
    Guid Id, Guid ProjectId, string EntityName, Guid? EntityId, ProjectChangeKind Kind,
    Guid? ChangedByUserId, DateTimeOffset ChangedAtUtc, IReadOnlyList<ProjectChangePropertyDto> Changes);

public sealed record ProjectHistoryPageDto(IReadOnlyList<ProjectChangeDto> Items, int Total, int Skip, int Take);
