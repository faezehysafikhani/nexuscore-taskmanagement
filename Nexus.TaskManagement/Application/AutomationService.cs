using System.Text.Json;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.Application.Common;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Nexus.TaskManagement.Application;

/// <summary>Working-time arithmetic for SLAs: a due moment N hours on, where only working days (the calendar's holidays and weekends are not) count when asked.</summary>
public static class SlaCalculator
{
    public static DateTimeOffset DueAt(DateTimeOffset start, int hours, bool businessDaysOnly, ISchedulingDayPolicy days)
    {
        if (!businessDaysOnly)
        {
            return start.AddHours(hours);
        }

        // Count the hours of working days only (24 a day); a non-working day's hours do not run.
        var remaining = TimeSpan.FromHours(hours);
        var cursor = start;
        for (var guard = 0; guard < 4000 && remaining > TimeSpan.Zero; guard++)
        {
            var day = DateOnly.FromDateTime(cursor.UtcDateTime);
            var endOfDay = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            if (!days.Check(day).IsAllowed)
            {
                cursor = endOfDay;
                continue;
            }

            var room = endOfDay - cursor;
            if (remaining <= room)
            {
                return cursor + remaining;
            }

            remaining -= room;
            cursor = endOfDay;
        }

        return cursor;
    }
}

/// <summary>The rule engine (phase 7). The actions are the task's own domain operations, applied directly - no permission or event of the user who caused the trigger is reused.</summary>
public sealed class TaskAutomationEngine(
    IAutomationRepository repository,
    ITagRepository tagRepository,
    ITaskStatusRepository statusRepository,
    ITaskManagementUnitOfWork unitOfWork,
    ISchedulingDayPolicy days,
    ITaskNoticePublisher notices,
    ILogger<TaskAutomationEngine> logger,
    IManagementChainProvider? managers = null) : ITaskAutomation
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    // One run at a time per request: an action's own change never starts the rules again.
    private bool _running;

    public async Task RunAsync(AutomationTrigger trigger, TaskItem task, CancellationToken cancellationToken)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        try
        {
            var rules = await repository.ListRulesAsync(task.TenantId, trigger, activeOnly: true, cancellationToken);
            var changed = false;
            foreach (var rule in rules.Take(50))
            {
                var conditions = Read<AutomationConditions>(rule.Conditions) ?? new AutomationConditions();
                if (!Matches(conditions, task))
                {
                    continue;
                }

                foreach (var action in Read<List<AutomationAction>>(rule.Actions) ?? [])
                {
                    try
                    {
                        var detail = await ApplyAsync(action, task, cancellationToken);
                        await repository.AddLogAsync(new AutomationLogEntry(Guid.NewGuid(), task.TenantId, rule.Id, rule.Name, task.Id, trigger, "Applied", detail, DateTimeOffset.UtcNow), cancellationToken);
                        changed = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Automation rule {Rule} failed on task {Task}.", rule.Name, task.Id);
                        await repository.AddLogAsync(new AutomationLogEntry(Guid.NewGuid(), task.TenantId, rule.Id, rule.Name, task.Id, trigger, "Failed", Trim(ex.Message), DateTimeOffset.UtcNow), cancellationToken);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Automation could not run for task {Task}.", task.Id);
        }
        finally
        {
            _running = false;
        }
    }

    private static bool Matches(AutomationConditions c, TaskItem task) =>
        (c.Priority is null || task.Priority == c.Priority)
        && (c.Status is null || task.Status == c.Status)
        && (c.IsProject is null || task.IsProject == c.IsProject)
        && (string.IsNullOrWhiteSpace(c.TitleContains) || task.Title.Contains(c.TitleContains.Trim(), StringComparison.OrdinalIgnoreCase));

    private async Task<string> ApplyAsync(AutomationAction action, TaskItem task, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        switch (action.Type)
        {
            case AutomationActionType.SetPriority when action.Priority is { } priority:
                task.SetPriority(priority);
                return $"Priority -> {priority}";

            case AutomationActionType.SetStatus when action.Status is { } status:
                task.ChangeStatus(status, now);
                return $"Status -> {status}";

            case AutomationActionType.AssignUser when action.UserId is { } userId:
                task.SetResponsibleUsers([userId]);
                return "Assigned";

            case AutomationActionType.AssignManager:
            {
                var who = task.AssignedUserId ?? task.OwnerUserId;
                if (managers is null || who is null)
                {
                    throw new InvalidOperationException("The organization structure is not available.");
                }

                var chain = await managers.GetChainAsync(task.TenantId, who.Value, cancellationToken);
                if (chain.Count == 0)
                {
                    throw new InvalidOperationException("The person has no manager in the structure.");
                }

                task.SetResponsibleUsers([chain[0]]);
                return "Assigned to the manager";
            }

            case AutomationActionType.AddTag when !string.IsNullOrWhiteSpace(action.Tag):
            {
                var name = action.Tag.Trim();
                var tag = await tagRepository.GetByNameAsync(task.TenantId, name, cancellationToken);
                if (tag is null)
                {
                    tag = new Tag(Guid.NewGuid(), task.TenantId, name);
                    await tagRepository.AddAsync(tag, cancellationToken);
                }

                if (await tagRepository.FindLinkAsync(tag.Id, task.Id, null, cancellationToken) is null)
                {
                    await tagRepository.AddLinkAsync(TaskTag.ForTask(Guid.NewGuid(), tag.Id, task.Id), cancellationToken);
                }

                return $"Tag {name}";
            }

            case AutomationActionType.StartSla when action.Hours is > 0:
            {
                var due = SlaCalculator.DueAt(now, action.Hours.Value, action.BusinessDaysOnly, days);
                task.StartSla(due);
                return $"SLA due {due:u}";
            }

            case AutomationActionType.Notify:
            {
                var recipient = action.UserId ?? task.OwnerUserId;
                if (recipient is null)
                {
                    throw new InvalidOperationException("Nobody to tell.");
                }

                await notices.PublishAsync(
                    new TaskNotice(task.TenantId, task.Id, task.Title, null, TaskNoticeKind.Activity, "Automation", Trim(action.Message ?? "A rule ran on this task."), [recipient.Value]),
                    cancellationToken);
                return "Notified";
            }

            default:
                throw new InvalidOperationException($"The action {action.Type} is missing what it needs.");
        }
    }

    private static T? Read<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Trim(string text) => text.Length <= 480 ? text : text[..480];
}

public sealed class AutomationService(
    IAutomationRepository repository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser) : IAutomationService
{
    public const int MaxRules = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static AutomationRuleDto ToDto(AutomationRule r) => new(
        r.Id, r.Name, r.Trigger,
        JsonSerializer.Deserialize<AutomationConditions>(r.Conditions, Json) ?? new AutomationConditions(),
        JsonSerializer.Deserialize<List<AutomationAction>>(r.Actions, Json) ?? [],
        r.SortOrder, r.IsActive);

    private static Error? Check(SaveAutomationRuleRequest request)
    {
        if (request.Actions is null || request.Actions.Count is < 1 or > 10)
        {
            return Error.Validation("A rule needs between 1 and 10 actions.");
        }

        foreach (var action in request.Actions)
        {
            var ok = action.Type switch
            {
                AutomationActionType.SetPriority => action.Priority is not null,
                AutomationActionType.SetStatus => action.Status is not null,
                AutomationActionType.AssignUser => action.UserId is not null,
                AutomationActionType.AddTag => !string.IsNullOrWhiteSpace(action.Tag) && action.Tag.Length <= 100,
                AutomationActionType.StartSla => action.Hours is > 0 and <= 100000,
                _ => Enum.IsDefined(action.Type)
            };
            if (!ok)
            {
                return Error.Validation($"The action {action.Type} is missing what it needs.");
            }
        }

        return null;
    }

    public async Task<Result<IReadOnlyList<AutomationRuleDto>>> ListAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is not { } tenantId
            ? Result.Failure<IReadOnlyList<AutomationRuleDto>>(Error.Unauthorized())
            : Result.Success<IReadOnlyList<AutomationRuleDto>>((await repository.ListRulesAsync(tenantId, null, false, cancellationToken)).Select(ToDto).ToList());

    public async Task<Result<AutomationRuleDto>> CreateAsync(SaveAutomationRuleRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<AutomationRuleDto>(Error.Unauthorized());
        }

        if (Check(request) is { } invalid)
        {
            return Result.Failure<AutomationRuleDto>(invalid);
        }

        if ((await repository.ListRulesAsync(tenantId, null, false, cancellationToken)).Count >= MaxRules)
        {
            return Result.Failure<AutomationRuleDto>(Error.Validation($"At most {MaxRules} rules."));
        }

        var rule = new AutomationRule(
            Guid.NewGuid(), tenantId, request.Name, request.Trigger,
            JsonSerializer.Serialize(request.Conditions ?? new AutomationConditions(), Json), JsonSerializer.Serialize(request.Actions, Json), request.SortOrder);
        if (!request.IsActive)
        {
            rule.Update(rule.Name, rule.Trigger, rule.Conditions, rule.Actions, rule.SortOrder, false);
        }

        await repository.AddRuleAsync(rule, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(rule));
    }

    public async Task<Result<AutomationRuleDto>> UpdateAsync(Guid id, SaveAutomationRuleRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<AutomationRuleDto>(Error.Unauthorized());
        }

        var rule = await repository.GetRuleAsync(tenantId, id, cancellationToken);
        if (rule is null)
        {
            return Result.Failure<AutomationRuleDto>(Error.NotFound("Rule not found."));
        }

        if (Check(request) is { } invalid)
        {
            return Result.Failure<AutomationRuleDto>(invalid);
        }

        rule.Update(request.Name, request.Trigger, JsonSerializer.Serialize(request.Conditions ?? new AutomationConditions(), Json), JsonSerializer.Serialize(request.Actions, Json), request.SortOrder, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(rule));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var rule = await repository.GetRuleAsync(tenantId, id, cancellationToken);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("Rule not found."));
        }

        repository.RemoveRule(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<AutomationLogDto>>> ListLogAsync(Guid? taskId, CancellationToken cancellationToken) =>
        currentUser.TenantId is not { } tenantId
            ? Result.Failure<IReadOnlyList<AutomationLogDto>>(Error.Unauthorized())
            : Result.Success<IReadOnlyList<AutomationLogDto>>((await repository.ListLogAsync(tenantId, taskId, 200, cancellationToken))
                .Select(l => new AutomationLogDto(l.Id, l.RuleId, l.RuleName, l.TaskId, l.Trigger, l.Outcome, l.Detail, l.AtUtc)).ToList());
}
