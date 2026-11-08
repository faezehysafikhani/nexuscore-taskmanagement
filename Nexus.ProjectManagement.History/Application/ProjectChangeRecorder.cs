using System.Text.Json;
using Nexus.ProjectManagement.History.Domain;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.History.Application;

/// <summary>
/// Turns what the modules write into project history. A change belongs to a project when it is the project
/// itself (the entity type Nexus.ProjectManagement.Core.Domain.Project - its own Id is the project id) or when
/// it has a non-empty ProjectId column. Everything else - users, chat, other tenants' data with no project - is ignored.
/// The system's own bookkeeping rows are left out (see <see cref="ExcludedEntities"/>) so history reads as what people did.
/// </summary>
public sealed class ProjectChangeRecorder(IProjectHistoryRepository repository, IProjectHistoryUnitOfWork unitOfWork)
    : IEntityChangeObserver
{
    public const string ProjectEntityTypeFullName = "Nexus.ProjectManagement.Core.Domain.Project";

    /// <summary>Written by the system as a side effect of something a person did (and already visible as that), or the history itself.</summary>
    public static readonly IReadOnlySet<string> ExcludedEntities = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(ProjectChange), "SprintEvent", "ScheduleBaselineActivity"
    };

    public async Task OnChangesSavedAsync(IReadOnlyList<EntityChange> changes, CancellationToken cancellationToken)
    {
        var rows = new List<ProjectChange>();
        foreach (var change in changes)
        {
            if (ExcludedEntities.Contains(change.EntityName) || ProjectIdOf(change) is not { } projectId)
            {
                continue;
            }

            rows.Add(new ProjectChange(
                Guid.NewGuid(),
                change.Values.TryGetValue("TenantId", out var tenant) && tenant is Guid tenantId && tenantId != Guid.Empty ? tenantId : null,
                projectId, change.EntityName, change.EntityId, (ProjectChangeKind)(int)change.Kind,
                change.UserId, change.OccurredAtUtc,
                change.Changes.Count == 0 ? null : JsonSerializer.Serialize(change.Changes.Select(c => new[] { c.Property, c.OldValue, c.NewValue }))));
        }

        if (rows.Count == 0)
        {
            return;
        }

        await repository.AddRangeAsync(rows, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    internal static Guid? ProjectIdOf(EntityChange change)
    {
        if (change.EntityTypeFullName == ProjectEntityTypeFullName)
        {
            return change.EntityId is { } id && id != Guid.Empty ? id : null;
        }

        return change.Values.TryGetValue("ProjectId", out var value) && value is Guid projectId && projectId != Guid.Empty
            ? projectId
            : null;
    }
}
