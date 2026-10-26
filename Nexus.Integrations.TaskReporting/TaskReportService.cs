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
/// is not otherwise on. Tasks.ManageAll holders cover the whole organization - or, where everyone
/// is kept to their own company (see IUserReachScope), the tasks of that company's people.
///
/// The subordinates' tasks are why this reads with IgnoreQueryFilters: Task Management's own
/// query filter would hide them. The tenant and the rule above are then applied here explicitly,
/// and the report only reads - opening such a task still goes through Task Management's rules.
///
/// Where tasks are given to posts (ITaskPostHistory), each row also tells who was on the task
/// when - and with which position then - and "anyone" above includes whoever was on it before.
/// </summary>
public sealed class TaskReportService(
    TaskManagementDbContext db,
    ITaskAccessScope access,
    IOrgStructureService orgStructure,
    IUserDirectory users,
    IUserReachScope? reachScope = null,
    ITaskPostHistory? history = null) : ITaskReportService
{
    /// <summary>Rows returned at most; the summary always counts every matching task.</summary>
    public const int MaxRows = 5000;

    private const int MaxDescriptionLength = 400;

    public async Task<Result<TaskReportScopeDto>> GetScopeAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var positionsByUser = await PositionTitlesAsync(tenantId, cancellationToken);
        var reach = await ReachAsync(tenantId, userId, cancellationToken);
        List<ReportScopePerson> people;
        if (!access.IsRestricted)
        {
            people = (await users.GetActiveUsersInTenantAsync(tenantId, cancellationToken))
                .Where(user => reach is null || reach.Contains(user.Id))
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
        if (access.IsRestricted || reach is not null)
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
        if (!access.IsRestricted && await ReachAsync(tenantId, userId, cancellationToken) is { } reach)
        {
            // Kept to one's own company: the tasks of its people (as on the task board).
            var people = reach.UserIds.ToList();
            var theirsBefore = await PastTaskIdsAsync(tenantId, people, null, _ => true, cancellationToken);
            tasks = tasks.Where(task =>
                (task.OwnerUserId == null && !task.OwnerHeldByPost)
                || (task.OwnerUserId != null && people.Contains(task.OwnerUserId.Value))
                || (task.AssignedUserId != null && people.Contains(task.AssignedUserId.Value))
                || task.Assignees.Any(a => people.Contains(a.UserId))
                || theirsBefore.Contains(task.Id));
        }

        if (access.IsRestricted)
        {
            var subordinates = (await SubordinatesAsync(tenantId, userId, cancellationToken)).Keys.ToList();
            // Tasks they were on before, through their posts (responsible, stand-in or creator).
            var theirsBefore = await PastTaskIdsAsync(tenantId, [userId, .. subordinates], null, _ => true, cancellationToken);
            tasks = tasks.Where(task =>
                theirsBefore.Contains(task.Id) ||
                task.OwnerUserId == null
                || task.OwnerUserId == userId
                || task.AssignedUserId == userId
                || task.Assignees.Any(a => a.UserId == userId)
                || (task.OwnerUserId != null && subordinates.Contains(task.OwnerUserId.Value))
                || (task.AssignedUserId != null && subordinates.Contains(task.AssignedUserId.Value))
                || task.Assignees.Any(a => a.IsResponsible && subordinates.Contains(a.UserId)));
        }

        // --- filters
        bool InRole(string periodRole) => role == "creator" ? periodRole == "creator" : periodRole != "creator";
        if (query.PersonId is { } personId)
        {
            var before = await PastTaskIdsAsync(tenantId, [personId], null, InRole, cancellationToken);
            tasks = role == "creator"
                ? tasks.Where(task => task.OwnerUserId == personId || before.Contains(task.Id))
                : tasks.Where(task => task.AssignedUserId == personId || task.Assignees.Any(a => a.IsResponsible && a.UserId == personId)
                    || before.Contains(task.Id));
        }

        if (query.PositionId is { } positionId)
        {
            var holders = await PositionHoldersAsync(tenantId, positionId, cancellationToken);
            // Also whoever had the position when they were on a task (they may have another one now).
            var before = await PastTaskIdsAsync(tenantId, null, positionId, InRole, cancellationToken);
            tasks = role == "creator"
                ? tasks.Where(task => (task.OwnerUserId != null && holders.Contains(task.OwnerUserId.Value)) || before.Contains(task.Id))
                : tasks.Where(task => (task.AssignedUserId != null && holders.Contains(task.AssignedUserId.Value))
                    || task.Assignees.Any(a => a.IsResponsible && holders.Contains(a.UserId))
                    || before.Contains(task.Id));
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

        var periods = history is null || loaded.Count == 0
            ? []
            : await history.ForTasksAsync(tenantId, loaded.Select(task => task.Id).ToList(), cancellationToken);
        var periodsOf = periods.GroupBy(period => period.TaskId).ToDictionary(group => group.Key, group => group.ToList());
        var peopleIds = loaded
            .SelectMany(task => task.ResponsibleUserIds.Append(task.OwnerUserId ?? Guid.Empty))
            .Concat(periods.Select(period => period.UserId))
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
                canOpen,
                HistoryOf(task, periodsOf.GetValueOrDefault(task.Id), id => names.GetValueOrDefault(id)?.DisplayName ?? "کاربر"));
        }).ToList();

        return Result.Success(new TaskReportDto(summary, rows, truncated));
    }

    /// <summary>
    /// Who was on the task when, oldest first. A done task's periods end when it was done at the
    /// latest, and those begun after it was done (its post changed hands since) are left out.
    /// </summary>
    private static List<ReportPeriod> HistoryOf(TaskItem task, List<TaskPostPeriodDto>? periods, Func<Guid, string> nameOf)
    {
        if (periods is null)
        {
            return [];
        }

        var doneAt = task.Status == TaskItemStatus.Completed ? task.ActualCompletionDateUtc : null;
        return periods
            .Where(period => doneAt is null || period.StartedAtUtc <= doneAt)
            .OrderBy(period => period.StartedAtUtc).ThenBy(period => period.Role)
            .Select(period => new ReportPeriod(
                period.UserId,
                nameOf(period.UserId),
                period.Role,
                period.PositionTitle,
                period.PostTitle,
                period.PostCode,
                period.Place,
                period.StartedAtUtc,
                doneAt is { } done && (period.EndedAtUtc is null || period.EndedAtUtc > done) ? done : period.EndedAtUtc))
            .ToList();
    }

    /// <summary>The tasks these people (null: anyone) - or whoever had this position then - were on before, through their posts.</summary>
    private async Task<List<Guid>> PastTaskIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid>? userIds, Guid? positionId, Func<string, bool> inRole, CancellationToken cancellationToken)
    {
        if (history is null)
        {
            return [];
        }

        return (await history.FindAsync(tenantId, userIds, positionId, cancellationToken))
            .Where(period => inRole(period.Role))
            .Select(period => period.TaskId)
            .Distinct()
            .ToList();
    }

    private async Task<UserReach?> ReachAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        reachScope is null ? null : await reachScope.GetReachAsync(tenantId, userId, cancellationToken);

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
