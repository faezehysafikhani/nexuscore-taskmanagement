using System.Text.Json;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>Task and project templates: a saved shape (title, subtasks, criteria, tags, due in N days) that makes a real task through <see cref="ITaskService"/>.</summary>
public sealed class TaskTemplateService(
    ITaskPlanningRepository repository,
    ITaskRepository taskRepository,
    ITaskService tasks,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser) : ITaskTemplateService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static TaskTemplateDto ToDto(TaskTemplate t) => new(t.Id, t.Name, t.IsProject, Read(t.Body));

    private static TaskTemplateBody Read(string json) =>
        JsonSerializer.Deserialize<TaskTemplateBody>(json, Json) ?? new TaskTemplateBody("", null, TaskPriority.Medium, false, 7, [], [], []);

    private static string Write(TaskTemplateBody body) => JsonSerializer.Serialize(Clean(body), Json);

    private static TaskTemplateBody Clean(TaskTemplateBody b) => b with
    {
        Title = b.Title.Trim(),
        DueInDays = Math.Clamp(b.DueInDays, 0, 3650),
        SubTasks = (b.SubTasks ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).Take(100).ToList(),
        Criteria = (b.Criteria ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).Take(50).ToList(),
        Tags = (b.Tags ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).Take(20).ToList()
    };

    public async Task<Result<IReadOnlyList<TaskTemplateDto>>> ListAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is not { } tenantId
            ? Result.Failure<IReadOnlyList<TaskTemplateDto>>(Error.Unauthorized())
            : Result.Success<IReadOnlyList<TaskTemplateDto>>((await repository.ListTemplatesAsync(tenantId, cancellationToken)).Select(ToDto).ToList());

    public async Task<Result<TaskTemplateDto>> CreateAsync(SaveTaskTemplateRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<TaskTemplateDto>(Error.Unauthorized());
        }

        if (await repository.GetTemplateByNameAsync(tenantId, request.Name, cancellationToken) is not null)
        {
            return Result.Failure<TaskTemplateDto>(Error.Conflict("A template with this name already exists."));
        }

        var template = new TaskTemplate(Guid.NewGuid(), tenantId, request.Name, request.Body.IsProject, Write(request.Body));
        await repository.AddTemplateAsync(template, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(template));
    }

    public async Task<Result<TaskTemplateDto>> UpdateAsync(Guid id, SaveTaskTemplateRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<TaskTemplateDto>(Error.Unauthorized());
        }

        var template = await repository.GetTemplateAsync(tenantId, id, cancellationToken);
        if (template is null)
        {
            return Result.Failure<TaskTemplateDto>(Error.NotFound("Template not found."));
        }

        var clash = await repository.GetTemplateByNameAsync(tenantId, request.Name, cancellationToken);
        if (clash is not null && clash.Id != id)
        {
            return Result.Failure<TaskTemplateDto>(Error.Conflict("A template with this name already exists."));
        }

        template.Update(request.Name, request.Body.IsProject, Write(request.Body));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(template));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var template = await repository.GetTemplateAsync(tenantId, id, cancellationToken);
        if (template is null)
        {
            return Result.Failure(Error.NotFound("Template not found."));
        }

        repository.RemoveTemplate(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<TaskTemplateDto>> SaveFromTaskAsync(Guid taskId, SaveTaskAsTemplateRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<TaskTemplateDto>(Error.Unauthorized());
        }

        var task = await taskRepository.GetByIdAsync(tenantId, taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskTemplateDto>(Error.NotFound("Task not found."));
        }

        if (await repository.GetTemplateByNameAsync(tenantId, request.Name, cancellationToken) is not null)
        {
            return Result.Failure<TaskTemplateDto>(Error.Conflict("A template with this name already exists."));
        }

        var criteria = await repository.ListCriteriaAsync(tenantId, taskId, cancellationToken);
        var due = (task.DueDate.DayNumber - DateOnly.FromDateTime(task.CreatedAtUtc.UtcDateTime).DayNumber);
        var body = new TaskTemplateBody(
            task.Title, task.Description, task.Priority, task.IsProject, Math.Max(0, due),
            task.SubTasks.OrderBy(s => s.SortOrder).Select(s => s.Title).ToList(),
            criteria.Select(c => c.Text).ToList(),
            task.Tags.Where(t => t.Tag is not null).Select(t => t.Tag!.Name).ToList());
        return await CreateAsync(new SaveTaskTemplateRequest(request.Name, body), cancellationToken);
    }

    public async Task<Result<TaskDto>> InstantiateAsync(Guid id, InstantiateTemplateRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var template = await repository.GetTemplateAsync(tenantId, id, cancellationToken);
        if (template is null)
        {
            return Result.Failure<TaskDto>(Error.NotFound("Template not found."));
        }

        var body = Read(template.Body);
        var assignee = request.AssignedUserId ?? userId;
        var subTasks = body.SubTasks.Count == 0 && !template.IsProject
            ? null
            : body.SubTasks.Select((title, i) => new SubTaskInput(title, SortOrder: i + 1, AssigneeUserIds: [assignee])).ToList();
        var created = await tasks.CreateAsync(
            new CreateTaskRequest(
                string.IsNullOrWhiteSpace(request.Title) ? body.Title : request.Title.Trim(),
                request.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(body.DueInDays)),
                body.Priority,
                body.Description,
                template.IsProject,
                AssignedUserId: assignee,
                SubTasks: subTasks,
                Tags: body.Tags.Count == 0 ? null : body.Tags),
            cancellationToken);
        if (created.IsFailure)
        {
            return created;
        }

        var order = 1;
        foreach (var text in body.Criteria)
        {
            await repository.AddCriterionAsync(new TaskCriterion(Guid.NewGuid(), tenantId, created.Value.Id, text, order++), cancellationToken);
        }

        if (body.Criteria.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return created;
    }
}
