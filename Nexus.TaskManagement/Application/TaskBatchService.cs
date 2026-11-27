using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Quick add and bulk actions, built on <see cref="ITaskService"/> so every rule (who may change what,
/// archived tasks, row versions, notifications) is the one-at-a-time rule - nothing is re-implemented here.
/// </summary>
public sealed class TaskBatchService(ITaskService tasks, ITaskRepository taskRepository, ITaskManagementUnitOfWork unitOfWork, ICurrentUserContext currentUser) : ITaskBatchService
{
    public const int MaxBulk = 100;

    public Task<Result<TaskDto>> QuickCreateAsync(QuickCreateTaskRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Task.FromResult(Result.Failure<TaskDto>(Error.Unauthorized()));
        }

        return tasks.CreateAsync(
            new CreateTaskRequest(
                request.Title.Trim(),
                request.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                request.Priority,
                AssignedUserId: userId),
            cancellationToken);
    }

    public async Task<Result<TaskDto>> FromMessageAsync(TaskFromMessageRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var text = request.Text.Trim();
        var title = string.IsNullOrWhiteSpace(request.Title) ? FirstLine(text) : request.Title.Trim();
        var created = await tasks.CreateAsync(
            new CreateTaskRequest(title, request.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow), TaskPriority.Medium, text, AssignedUserId: userId),
            cancellationToken);
        if (created.IsFailure)
        {
            return created;
        }

        var task = await taskRepository.GetForUpdateAsync(tenantId, created.Value.Id, cancellationToken);
        task?.SetSource("ChatMessage", request.MessageId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(created.Value with { SourceType = "ChatMessage", SourceId = request.MessageId });
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length <= 200 ? line : line[..200];
    }

    public async Task<Result<BulkTaskResult>> BulkAsync(BulkTaskRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<BulkTaskResult>(Error.Unauthorized());
        }

        var ids = request.Ids.Distinct().ToList();
        if (ids.Count == 0 || ids.Count > MaxBulk)
        {
            return Result.Failure<BulkTaskResult>(Error.Validation($"Send between 1 and {MaxBulk} tasks."));
        }

        var missing = request.Action switch
        {
            BulkTaskAction.Status when request.Status is null => "Status",
            BulkTaskAction.CustomStatus when request.CustomStatusId is null => "CustomStatusId",
            BulkTaskAction.Priority when request.Priority is null => "Priority",
            _ => null
        };
        if (missing is not null)
        {
            return Result.Failure<BulkTaskResult>(Error.Validation($"{missing} is required for this action."));
        }

        var succeeded = new List<Guid>();
        var failed = new List<BulkTaskFailure>();
        foreach (var id in ids)
        {
            var result = request.Action switch
            {
                BulkTaskAction.Status => await tasks.ChangeStatusAsync(id, new ChangeTaskStatusRequest(request.Status), cancellationToken),
                BulkTaskAction.CustomStatus => await tasks.ChangeStatusAsync(id, new ChangeTaskStatusRequest(CustomStatusId: request.CustomStatusId), cancellationToken),
                BulkTaskAction.Priority => await tasks.ChangePriorityAsync(id, new ChangeTaskPriorityRequest(request.Priority!.Value), cancellationToken),
                BulkTaskAction.Archive => await tasks.ArchiveAsync(id, cancellationToken),
                _ => await tasks.RestoreAsync(id, cancellationToken)
            };

            if (result.IsSuccess)
            {
                succeeded.Add(id);
            }
            else
            {
                failed.Add(new BulkTaskFailure(id, result.Error.Code, result.Error.Message));
            }
        }

        return Result.Success(new BulkTaskResult(succeeded, failed));
    }
}
