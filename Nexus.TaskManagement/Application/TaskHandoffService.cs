using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

public sealed record HandoffTaskRequest(Guid? ToUserId, Guid? ToGroupId, string Note);

public interface ITaskHandoffService
{
    Task<Result> HandoffAsync(Guid taskId, HandoffTaskRequest request, CancellationToken cancellationToken);
}

/// <summary>Hand-off between teams (CAT-110): the task goes to another person and/or team, with a note that stays on it as a comment.</summary>
public sealed class TaskHandoffService(
    ITaskRepository taskRepository,
    ITaskCommentRepository comments,
    ITaskAccessScope access,
    ITaskActivityService activity,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    NexusCore.Application.Identity.Interfaces.ITaskPosts? taskPosts = null) : ITaskHandoffService
{
    public async Task<Result> HandoffAsync(Guid taskId, HandoffTaskRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } me) return Result.Failure(Error.Unauthorized());
        if (request.ToUserId is null && request.ToGroupId is null) return Result.Failure(Error.Validation("گیرنده‌ی تحویل (فرد یا تیم) را مشخص کنید."));
        if (string.IsNullOrWhiteSpace(request.Note)) return Result.Failure(Error.Validation("توضیح تحویل را بنویسید."));
        if (request.Note.Length > 2000) return Result.Failure(Error.Validation("توضیح تحویل بیش از حد طولانی است."));

        var task = await taskRepository.GetByIdAsync(tenantId, taskId, cancellationToken);
        if (task is null) return Result.Failure(Error.NotFound("فعالیت پیدا نشد."));
        if (!(access.CanManage(task) || task.IsResponsible(me)))
            return Result.Failure(Error.Forbidden("فقط مالک، مسئول یا مدیر فعالیت می‌تواند آن را تحویل دهد."));
        if (task.Status == TaskItemStatus.Completed) return Result.Failure(Error.Validation("فعالیت تمام‌شده را نمی‌توان تحویل داد."));

        if (request.ToUserId is { } user) task.SetResponsibleUsers([user]);
        if (request.ToGroupId is { } group) task.SetAssignedGroup(group);

        await comments.AddAsync(new TaskComment(Guid.NewGuid(), tenantId, taskId, me, $"تحویل کار: {request.Note.Trim()}"), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Where tasks are given to posts, the responsible post follows the person: the new person's main post.
        if (request.ToUserId is { } to && taskPosts is { Enabled: true }
            && (await taskPosts.GetMainPostsAsync(tenantId, [to], cancellationToken)).TryGetValue(to, out var post))
        {
            await taskPosts.SetTaskPostsAsync(tenantId, taskId, null, [post], null, cancellationToken);
        }

        await activity.RecordAsync(taskId, "Task handed off", request.Note.Trim(), cancellationToken);
        return Result.Success();
    }
}
