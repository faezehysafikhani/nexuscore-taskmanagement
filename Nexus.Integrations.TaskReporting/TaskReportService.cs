using Microsoft.EntityFrameworkCore;
using Nexus.OrgStructure.Application;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.TaskReporting;

public interface ITaskReportService
{
    Task<Result<TaskReportScopeDto>> GetScopeAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<Result<TaskReportDto>> GetReportAsync(Guid tenantId, Guid userId, TaskReportQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Which tasks a report covers: every task the caller can already see (owner, responsible, on
/// its access list, or ownerless), plus every task created by or assigned to anyone below them in
/// the organization structure, at any depth and whatever their position - even tasks the caller
/// is not otherwise on. Tasks.ManageAll holders cover the whole organization.
///
/// The subordinates' tasks are why this reads with IgnoreQueryFilters: Task Management's own
/// query filter would hide them. The tenant and the rule above are then applied here explicitly,
/// and the report only reads - opening such a task still goes through Task Management's rules.
/// </summary>
public sealed class TaskReportService(
    TaskManagementDbContext db,
    ITaskAccessScope access,
    IOrgStructureService orgStructure,
    IUserDirectory users) : ITaskReportService
{
    /// <summary>Rows returned at most; the summary always counts every matching task.</summary>
    public const int MaxRows = 5000;

    private const int MaxDescriptionLength = 400;

    public async Task<Result<TaskReportScopeDto>> GetScopeAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var positionsByUser = await PositionTitlesAsync(tenantId, cancellationToken);
        List<ReportScopePerson> people;
        if (!access.IsRestricted)
        {
            people = (await users.GetActiveUsersInTenantAsync(tenantId, cancellationToken))
                .OrderBy(user => user.DisplayName)
                .Select(user => new ReportScopePerson(user.Id, user.DisplayName, positionsByUser.GetValueOrDefault(user.Id), user.Id == userId ? "self" : "all", user.Id == userId ? 0 : 1))
                .ToList();
        }
        else
        {
            var subordinates = await SubordinatesAsync(tenantId, userId, cancellationToken);
            var names = (await users.GetUsersAsync([userId, .. subordinates.Keys], cancellationToken)).ToDictionary(user => user.Id);
            people = [new ReportScopePerson(userId, names.GetValueOrDefault(userId)?.DisplayName ?? string.Empty, positionsByUser.GetValueOrDefault(userId), "self", 0)];
            people.AddRange(subordinates
                .OrderBy(pair => pair.Value).ThenBy(pair => names.GetValueOrDefault(pair.Key)?.DisplayName)
                .Select(pair => new ReportScopePerson(
                    pair.Key,
                    names.GetValueOrDefault(pair.Key)?.DisplayName ?? "کاربر",
                    positionsByUser.GetValueOrDefault(pair.Key),
                    pair.Value == 1 ? "direct" : "indirect",
                    pair.Value)));
        }

        var positions = (await orgStructure.ListPositionsAsync(tenantId, cancellationToken)).Value ?? [];
        if (access.IsRestricted)
        {
            // Only the positions of the companies of the people in the report (not other companies').
            var inScope = people.Select(person => person.UserId).ToHashSet();
            var companies = ((await orgStructure.ListAssignmentsAsync(tenantId, null, null, cancellationToken)).Value ?? [])
                .Where(a => inScope.Contains(a.UserId))
                .Select(a => a.CompanyId)
                .ToHashSet();
            positions = positions.Where(p => p.CompanyIds is { Count: > 0 } ids ? ids.Any(companies.Contains) : companies.Count > 0).ToList();
        }
        var tags = await db.Tags.AsNoTracking()
            .Where(tag => tag.TenantId == tenantId)
            .OrderBy(tag => tag.Name)
            .Select(tag => tag.Name)
            .ToListAsync(cancellationToken);

        return Result.Success(new TaskReportScopeDto(
            !access.IsRestricted,
            people,
            positions.Where(p => p.IsActive).Select(p => new ReportPosition(p.Id, p.Title)).ToList(),
            tags));
    }

    public async Task<Result<TaskReportDto>> GetReportAsync(Guid tenantId, Guid userId, TaskReportQuery query, CancellationToken cancellationToken)
    {
        var role = (query.PersonRole ?? "responsible").Trim().ToLowerInvariant();
        if (role is not ("responsible" or "creator"))
        {
            return Result.Failure<TaskReportDto>(Error.Validation("Person role must be responsible or creator."));
        }

        var type = query.Type?.Trim().ToLowerInvariant();
        if (type is not (null or "" or "normal" or "project" or "recurring"))
        {
            return Result.Failure<TaskReportDto>(Error.Validation("Unknown task type."));
        }

        var statuses = new List<TaskItemStatus>();
        foreach (var status in query.Statuses.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (ParseStatus(status) is not { } parsed)
            {
                return Result.Failure<TaskReportDto>(Error.Validation("Unknown task status."));
            }

            statuses.Add(parsed);
        }

        if (query.DueFrom is { } from && query.DueTo is { } to && from > to)
        {
            return Result.Failure<TaskReportDto>(Error.Validation("The start date is after the end date."));
        }

        // --- whose tasks
        var tasks = db.Tasks.IgnoreQueryFilters().AsNoTracking().Where(task => task.TenantId == tenantId);
        if (access.IsRestricted)
        {
            var subordinates = (await SubordinatesAsync(tenantId, userId, cancellationToken)).Keys.ToList();
            tasks = tasks.Where(task =>
                task.OwnerUserId == null
                || task.OwnerUserId == userId
                || task.AssignedUserId == userId
                || task.Assignees.Any(a => a.UserId == userId)
                || (task.OwnerUserId != null && subordinates.Contains(task.OwnerUserId.Value))
                || (task.AssignedUserId != null && subordinates.Contains(task.AssignedUserId.Value))
                || task.Assignees.Any(a => a.IsResponsible && subordinates.Contains(a.UserId)));
        }

        // --- filters
        if (query.PersonId is { } personId)
        {
            tasks = role == "creator"
                ? tasks.Where(task => task.OwnerUserId == personId)
                : tasks.Where(task => task.AssignedUserId == personId || task.Assignees.Any(a => a.IsResponsible && a.UserId == personId));
        }

        if (query.PositionId is { } positionId)
        {
            var holders = await PositionHoldersAsync(tenantId, positionId, cancellationToken);
            tasks = role == "creator"
                ? tasks.Where(task => task.OwnerUserId != null && holders.Contains(task.OwnerUserId.Value))
                : tasks.Where(task => (task.AssignedUserId != null && holders.Contains(task.AssignedUserId.Value))
                    || task.Assignees.Any(a => a.IsResponsible && holders.Contains(a.UserId)));
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var normalized = query.Tag.Trim().ToUpperInvariant();
            tasks = tasks.Where(task => task.Tags.Any(link => link.Tag!.NormalizedName == normalized));
        }

        // A recurring task counts as recurring even when it is also marked a project.
        tasks = type switch
        {
            "recurring" => tasks.Where(task => task.Recurrence != null),
            "project" => tasks.Where(task => task.Recurrence == null && task.IsProject),
            "normal" => tasks.Where(task => task.Recurrence == null && !task.IsProject),
            _ => tasks
        };

        if (statuses.Count > 0)
        {
            tasks = tasks.Where(task => statuses.Contains(task.Status));
        }

        if (query.DueFrom is { } dueFrom)
        {
            tasks = tasks.Where(task => task.DueDate >= dueFrom);
        }

        if (query.DueTo is { } dueTo)
        {
            tasks = tasks.Where(task => task.DueDate <= dueTo);
        }

        // --- summary over every match
        var today = IranToday();
        var byStatus = await tasks.GroupBy(task => task.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        int CountOf(TaskItemStatus s) => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        var total = byStatus.Sum(x => x.Count);
        var recurring = await tasks.CountAsync(task => task.Recurrence != null, cancellationToken);
        var project = await tasks.CountAsync(task => task.Recurrence == null && task.IsProject, cancellationToken);
        var overdue = await tasks.CountAsync(task =>
            task.Recurrence == null
            && (task.Status == TaskItemStatus.Todo || task.Status == TaskItemStatus.InProgress)
            && task.DueDate < today, cancellationToken);

        var summary = new TaskReportSummary(
            total,
            CountOf(TaskItemStatus.Todo),
            CountOf(TaskItemStatus.InProgress),
            CountOf(TaskItemStatus.Paused),
            CountOf(TaskItemStatus.Completed),
            overdue,
            total - recurring - project,
            project,
            recurring);

        // --- rows
        var loaded = await tasks
            .Include(task => task.Assignees)
            .Include(task => task.Tags).ThenInclude(link => link.Tag)
            .Include(task => task.Recurrence)
            .OrderBy(task => task.DueDate).ThenBy(task => task.Title)
            .Take(MaxRows + 1)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var truncated = loaded.Count > MaxRows;
        if (truncated)
        {
            loaded.RemoveAt(loaded.Count - 1);
        }

        var peopleIds = loaded
            .SelectMany(task => task.ResponsibleUserIds.Append(task.OwnerUserId ?? Guid.Empty))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var names = peopleIds.Count == 0
            ? new Dictionary<Guid, UserContact>()
            : (await users.GetUsersAsync(peopleIds, cancellationToken)).ToDictionary(user => user.Id);
        var positionsByUser = await PositionTitlesAsync(tenantId, cancellationToken);
        ReportPerson Person(Guid id) => new(id, names.GetValueOrDefault(id)?.DisplayName ?? "کاربر", positionsByUser.GetValueOrDefault(id));

        var rows = loaded.Select(task =>
        {
            var isRecurring = task.Recurrence is not null;
            var canOpen = !access.IsRestricted
                || task.OwnerUserId == null
                || task.OwnerUserId == userId
                || task.AssignedUserId == userId
                || task.Assignees.Any(a => a.UserId == userId);
            return new TaskReportRow(
                task.Id,
                task.Title,
                task.Description is { Length: > MaxDescriptionLength } d ? d[..MaxDescriptionLength] + "…" : task.Description,
                StatusName(task.Status),
                isRecurring ? "recurring" : task.IsProject ? "project" : "normal",
                task.Priority switch { TaskPriority.Low => "low", TaskPriority.High => "high", _ => "medium" },
                task.DueDate.ToString("yyyy-MM-dd"),
                task.DueTime?.ToString("HH:mm"),
                !isRecurring && task.Status is TaskItemStatus.Todo or TaskItemStatus.InProgress && task.DueDate < today,
                task.CreatedAtUtc,
                task.ActualCompletionDateUtc,
                task.OwnerUserId is { } owner ? Person(owner) : null,
                task.ResponsibleUserIds.Select(Person).ToList(),
                task.Tags.Where(link => link.Tag is not null).Select(link => link.Tag!.Name).OrderBy(name => name).ToList(),
                canOpen);
        }).ToList();

        return Result.Success(new TaskReportDto(summary, rows, truncated));
    }

    /// <summary>Everyone below the user, with their depth (1 = direct). Empty without a place in the structure.</summary>
    private async Task<Dictionary<Guid, int>> SubordinatesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var result = await orgStructure.GetSubordinatesAsync(tenantId, userId, includeIndirect: true, cancellationToken);
        return (result.Value ?? []).ToDictionary(s => s.UserId, s => s.Depth);
    }

    private async Task<Dictionary<Guid, string>> PositionTitlesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var assignments = await orgStructure.ListAssignmentsAsync(tenantId, null, null, cancellationToken);
        return (assignments.Value ?? [])
            .Where(a => a.PositionTitle is not null)
            .ToDictionary(a => a.UserId, a => a.PositionTitle!);
    }

    private async Task<List<Guid>> PositionHoldersAsync(Guid tenantId, Guid positionId, CancellationToken cancellationToken)
    {
        var assignments = await orgStructure.ListAssignmentsAsync(tenantId, null, null, cancellationToken);
        return (assignments.Value ?? []).Where(a => a.PositionId == positionId).Select(a => a.UserId).ToList();
    }

    /// <summary>Today in Iran (UTC+03:30), the calendar every date in the product is shown in.</summary>
    private static DateOnly IranToday() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3.5));

    private static TaskItemStatus? ParseStatus(string value) => value.Trim().ToLowerInvariant() switch
    {
        "todo" => TaskItemStatus.Todo,
        "in_progress" => TaskItemStatus.InProgress,
        "paused" => TaskItemStatus.Paused,
        "completed" => TaskItemStatus.Completed,
        _ => null
    };

    private static string StatusName(TaskItemStatus status) => status switch
    {
        TaskItemStatus.InProgress => "in_progress",
        TaskItemStatus.Paused => "paused",
        TaskItemStatus.Completed => "completed",
        _ => "todo"
    };
}
