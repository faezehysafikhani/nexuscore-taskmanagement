using System.Text.Json;
using Nexus.ProjectManagement.History.Application.Dtos;
using Nexus.ProjectManagement.History.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.History.Application;

public interface IProjectHistoryService
{
    Task<Result<ProjectHistoryPageDto>> QueryAsync(ProjectHistoryQuery query, CancellationToken cancellationToken);
}

public sealed class ProjectHistoryService(IProjectHistoryRepository repository) : IProjectHistoryService
{
    public const int MaxPageSize = 200;

    public async Task<Result<ProjectHistoryPageDto>> QueryAsync(ProjectHistoryQuery query, CancellationToken cancellationToken)
    {
        if (query.ProjectId == Guid.Empty)
        {
            return Result.Failure<ProjectHistoryPageDto>(Error.Validation("A project is required."));
        }

        if (query.From is not null && query.To is not null && query.To < query.From)
        {
            return Result.Failure<ProjectHistoryPageDto>(Error.Validation("'to' cannot be before 'from'."));
        }

        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, MaxPageSize);
        var (items, total) = await repository.QueryAsync(query with { Skip = skip, Take = take }, cancellationToken);

        return Result.Success(new ProjectHistoryPageDto(items.Select(ToDto).ToList(), total, skip, take));
    }

    private static ProjectChangeDto ToDto(ProjectChange change) => new(
        change.Id, change.ProjectId, change.EntityName, change.EntityId, change.Kind, change.ChangedByUserId, change.ChangedAtUtc, Parse(change.ChangesJson));

    private static IReadOnlyList<ProjectChangePropertyDto> Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return (JsonSerializer.Deserialize<List<string?[]>>(json) ?? [])
                .Where(p => p.Length == 3 && p[0] is not null)
                .Select(p => new ProjectChangePropertyDto(p[0]!, p[1], p[2]))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
