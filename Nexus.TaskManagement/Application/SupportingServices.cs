using NexusCore.Application.Files;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

// ---------------------------------------------------------------------------
// Recurrence schedules
// ---------------------------------------------------------------------------

public sealed class RepetitiveTaskService(
    IRepetitiveTaskRepository repository,
    ITaskRepository taskRepository,
    IRecurrenceCalculator calculator,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    ITaskAccessScope access) : IRepetitiveTaskService
{
    /// <summary>A schedule is part of its task: only the task's owner (or Tasks.ManageAll) changes it.</summary>
    private async Task<bool> CanManageTaskAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await taskRepository.GetForUpdateAsync(tenantId, taskId, cancellationToken) is { } task && access.CanManage(task);

    public async Task<Result<PagedResult<RepetitiveTaskDto>>> ListAsync(
        ListRepetitiveTasksRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<PagedResult<RepetitiveTaskDto>>(Error.Unauthorized());
        }

        var normalized = request with
        {
            TenantId = currentUser.TenantId.Value,
            PageNumber = Math.Max(1, request.PageNumber),
            PageSize = Math.Clamp(request.PageSize, 1, 200)
        };

        var page = await repository.ListAsync(normalized, cancellationToken);
        return Result.Success(new PagedResult<RepetitiveTaskDto>(
            page.Items.Select(TaskService.ToDto).ToList(),
            page.PageNumber, page.PageSize, page.TotalCount));
    }

    public async Task<Result<RepetitiveTaskDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<RepetitiveTaskDto>(Error.Unauthorized());
        }

        var schedule = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        return schedule is null
            ? Result.Failure<RepetitiveTaskDto>(Error.NotFound("Recurrence schedule not found."))
            : Result.Success(TaskService.ToDto(schedule));
    }

    public async Task<Result<RepetitiveTaskDto>> CreateAsync(
        CreateRepetitiveTaskRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<RepetitiveTaskDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;

        var task = await taskRepository.GetForUpdateAsync(tenantId, request.TaskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<RepetitiveTaskDto>(Error.NotFound("Task not found."));
        }

        if (!access.CanManage(task))
        {
            return Result.Failure<RepetitiveTaskDto>(TaskService.NotTaskOwner());
        }

        // One schedule per task - the database enforces it too, but failing here gives a
        // usable message instead of a unique-index violation.
        var existing = await repository.GetByTaskIdAsync(tenantId, request.TaskId, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<RepetitiveTaskDto>(
                Error.Conflict("This task already has a recurrence schedule. Update it instead."));
        }

        var schedule = new RepetitiveTask(
            Guid.NewGuid(), tenantId, request.TaskId,
            request.Recurrence.Frequency, request.Recurrence.StartDate);
        TaskService.ApplyRecurrence(schedule, request.Recurrence);
        schedule.SetNextExecution(calculator.CalculateNextExecution(schedule, DateTimeOffset.UtcNow));

        await repository.AddAsync(schedule, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(TaskService.ToDto(schedule));
    }

    public async Task<Result<RepetitiveTaskDto>> UpdateAsync(
        Guid id, UpdateRepetitiveTaskRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<RepetitiveTaskDto>(Error.Unauthorized());
        }

        var schedule = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        if (schedule is null)
        {
            return Result.Failure<RepetitiveTaskDto>(Error.NotFound("Recurrence schedule not found."));
        }

        if (!await CanManageTaskAsync(currentUser.TenantId.Value, schedule.TaskId, cancellationToken))
        {
            return Result.Failure<RepetitiveTaskDto>(TaskService.NotTaskOwner());
        }

        TaskService.ApplyRecurrence(schedule, request.Recurrence);
        schedule.SetNextExecution(calculator.CalculateNextExecution(schedule, DateTimeOffset.UtcNow));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(TaskService.ToDto(schedule));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var schedule = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        if (schedule is null)
        {
            return Result.Failure(Error.NotFound("Recurrence schedule not found."));
        }

        if (!await CanManageTaskAsync(currentUser.TenantId.Value, schedule.TaskId, cancellationToken))
        {
            return Result.Failure(TaskService.NotTaskOwner());
        }

        repository.Remove(schedule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<RepetitiveTaskDto>> SetActiveAsync(
        Guid id, bool isActive, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<RepetitiveTaskDto>(Error.Unauthorized());
        }

        var schedule = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        if (schedule is null)
        {
            return Result.Failure<RepetitiveTaskDto>(Error.NotFound("Recurrence schedule not found."));
        }

        if (!await CanManageTaskAsync(currentUser.TenantId.Value, schedule.TaskId, cancellationToken))
        {
            return Result.Failure<RepetitiveTaskDto>(TaskService.NotTaskOwner());
        }

        if (isActive)
        {
            schedule.Activate();
            schedule.SetNextExecution(calculator.CalculateNextExecution(schedule, DateTimeOffset.UtcNow));
        }
        else
        {
            schedule.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(TaskService.ToDto(schedule));
    }
}

// ---------------------------------------------------------------------------
// Tags
// ---------------------------------------------------------------------------

public sealed class TagService(
    ITagRepository repository,
    ITaskRepository taskRepository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser) : ITagService
{
    public async Task<Result<IReadOnlyList<TagDto>>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<IReadOnlyList<TagDto>>(Error.Unauthorized());
        }

        var tags = await repository.ListAsync(currentUser.TenantId.Value, search, cancellationToken);
        return Result.Success<IReadOnlyList<TagDto>>(tags.Select(TaskService.ToDto).ToList());
    }

    public async Task<Result<TagDto>> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TagDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        if (await repository.GetByNameAsync(tenantId, request.Name, cancellationToken) is not null)
        {
            return Result.Failure<TagDto>(Error.Conflict("A tag with this name already exists."));
        }

        var tag = new Tag(Guid.NewGuid(), tenantId, request.Name, request.Color);
        await repository.AddAsync(tag, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(TaskService.ToDto(tag));
    }

    public async Task<Result<TagDto>> UpdateAsync(Guid id, UpdateTagRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TagDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var tag = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (tag is null)
        {
            return Result.Failure<TagDto>(Error.NotFound("Tag not found."));
        }

        var clash = await repository.GetByNameAsync(tenantId, request.Name, cancellationToken);
        if (clash is not null && clash.Id != id)
        {
            return Result.Failure<TagDto>(Error.Conflict("A tag with this name already exists."));
        }

        tag.Rename(request.Name);
        tag.SetColor(request.Color);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(TaskService.ToDto(tag));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var tag = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        if (tag is null)
        {
            return Result.Failure(Error.NotFound("Tag not found."));
        }

        // TaskTags cascades from Tags, so the links go with it.
        repository.Remove(tag);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public Task<Result> AssignToTaskAsync(Guid taskId, AssignTagRequest request, CancellationToken cancellationToken) =>
        AssignAsync(request.TagId, taskId, null, cancellationToken);

    public Task<Result> AssignToSubTaskAsync(Guid subTaskId, AssignTagRequest request, CancellationToken cancellationToken) =>
        AssignAsync(request.TagId, null, subTaskId, cancellationToken);

    private async Task<Result> AssignAsync(
        Guid tagId, Guid? taskId, Guid? subTaskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        if (await repository.GetByIdAsync(tenantId, tagId, cancellationToken) is null)
        {
            return Result.Failure(Error.NotFound("Tag not found."));
        }

        if (taskId is { } t && await taskRepository.GetForUpdateAsync(tenantId, t, cancellationToken) is null)
        {
            return Result.Failure(Error.NotFound("Task not found."));
        }

        if (subTaskId is { } s && await taskRepository.GetSubTaskAsync(tenantId, s, cancellationToken) is null)
        {
            return Result.Failure(Error.NotFound("Subtask not found."));
        }

        if (await repository.FindLinkAsync(tagId, taskId, subTaskId, cancellationToken) is not null)
        {
            return Result.Success();
        }

        var link = taskId is { } taskValue
            ? TaskTag.ForTask(Guid.NewGuid(), tagId, taskValue)
            : TaskTag.ForSubTask(Guid.NewGuid(), tagId, subTaskId!.Value);

        await repository.AddLinkAsync(link, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public Task<Result> RemoveFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken) =>
        RemoveAsync(tagId, taskId, null, cancellationToken);

    public Task<Result> RemoveFromSubTaskAsync(Guid subTaskId, Guid tagId, CancellationToken cancellationToken) =>
        RemoveAsync(tagId, null, subTaskId, cancellationToken);

    private async Task<Result> RemoveAsync(
        Guid tagId, Guid? taskId, Guid? subTaskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var link = await repository.FindLinkAsync(tagId, taskId, subTaskId, cancellationToken);
        if (link is null)
        {
            return Result.Success();
        }

        // One row may carry both owners. Detach only the side being removed and keep the row
        // alive while the other side still points at something - deleting outright would
        // silently drop the surviving link.
        if (taskId is not null)
        {
            link.DetachTask();
        }
        else
        {
            link.DetachSubTask();
        }

        if (link.IsOrphaned)
        {
            repository.RemoveLink(link);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Files
// ---------------------------------------------------------------------------

public sealed class TaskFileService(
    ITaskFileRepository repository,
    ITaskRepository taskRepository,
    ITaskCommentRepository commentRepository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IFileStorage fileStorage) : ITaskFileService
{
    public Task<Result<TaskFileDto>> UploadToTaskAsync(
        Guid taskId, UploadFileRequest request, CancellationToken cancellationToken) =>
        UploadAsync(request, taskId, null, null, cancellationToken);

    public Task<Result<TaskFileDto>> UploadToSubTaskAsync(
        Guid subTaskId, UploadFileRequest request, CancellationToken cancellationToken) =>
        UploadAsync(request, null, subTaskId, null, cancellationToken);

    public Task<Result<TaskFileDto>> UploadToCommentAsync(
        Guid commentId, UploadFileRequest request, CancellationToken cancellationToken) =>
        UploadAsync(request, null, null, commentId, cancellationToken);

    private async Task<Result<TaskFileDto>> UploadAsync(
        UploadFileRequest request, Guid? taskId, Guid? subTaskId, Guid? commentId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TaskFileDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;

        if (request.Content is null || request.Content.Length == 0)
        {
            return Result.Failure<TaskFileDto>(Error.Validation("The uploaded file is empty."));
        }

        // Measured from the bytes actually received, never from a client-supplied length.
        if (request.Content.Length > TaskFileAsset.MaxFileSizeBytes)
        {
            return Result.Failure<TaskFileDto>(Error.Validation(
                $"File is too large. The maximum size is {TaskFileAsset.MaxFileSizeBytes} bytes (200 KB)."));
        }

        if (taskId is { } t && await taskRepository.GetForUpdateAsync(tenantId, t, cancellationToken) is null)
        {
            return Result.Failure<TaskFileDto>(Error.NotFound("Task not found."));
        }

        if (subTaskId is { } s && await taskRepository.GetSubTaskAsync(tenantId, s, cancellationToken) is null)
        {
            return Result.Failure<TaskFileDto>(Error.NotFound("Subtask not found."));
        }

        if (commentId is { } c)
        {
            var comment = await commentRepository.GetByIdAsync(tenantId, c, cancellationToken);
            if (comment is null)
            {
                return Result.Failure<TaskFileDto>(Error.NotFound("Comment not found."));
            }

            // Attachments are part of what the author wrote; nobody else can add to it.
            if (comment.UserId != currentUser.UserId)
            {
                return Result.Failure<TaskFileDto>(Error.Forbidden("You can only attach files to your own comments."));
            }
        }

        // The client's filename is display data only. The bytes go through the platform file
        // storage, which generates its own key, so a crafted name cannot escape the storage
        // folder or overwrite anything.
        var displayName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "file";
        }

        var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType;

        StoredFile stored;
        await using (var content = new MemoryStream(request.Content, writable: false))
        {
            stored = await fileStorage.SaveAsync(displayName, contentType, content, cancellationToken);
        }

        var fileId = Guid.NewGuid();
        var asset = new TaskFileAsset(
            fileId, tenantId,
            displayName,
            stored.StorageKey,
            contentType,
            request.Content.Length,
            stored.StorageKey,
            currentUser.UserId);

        var link = taskId is { } taskValue
            ? TaskFile.ForTask(Guid.NewGuid(), fileId, taskValue)
            : subTaskId is { } subTaskValue
                ? TaskFile.ForSubTask(Guid.NewGuid(), fileId, subTaskValue)
                : TaskFile.ForComment(Guid.NewGuid(), fileId, commentId!.Value);

        await repository.AddAsync(asset, link, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The row was not written, so the stored bytes would be unreachable: remove them.
            await fileStorage.DeleteAsync(stored.StorageKey, cancellationToken);
            throw;
        }

        return Result.Success(new TaskFileDto(
            link.Id, asset.Id, asset.OriginalFileName, asset.ContentType,
            asset.FileSizeBytes, asset.UploadedByUserId, asset.CreatedAtUtc));
    }

    public async Task<Result<FileDownload>> DownloadAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<FileDownload>(Error.Unauthorized());
        }

        var asset = await repository.GetAssetAsync(currentUser.TenantId.Value, fileId, cancellationToken);
        // Knowing a file's id is not enough: it must belong to something the caller can see.
        if (asset is null || !await repository.IsReachableAsync(fileId, cancellationToken))
        {
            return Result.Failure<FileDownload>(Error.NotFound("File not found."));
        }

        await using var stream = await fileStorage.OpenReadAsync(asset.StoragePath, cancellationToken);
        if (stream is null)
        {
            return Result.Failure<FileDownload>(
                Error.NotFound("The file record exists but its contents are missing from storage."));
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return Result.Success(new FileDownload(asset.OriginalFileName, asset.ContentType, buffer.ToArray()));
    }

    public async Task<Result> DeleteAsync(Guid linkId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var link = await repository.GetLinkAsync(linkId, cancellationToken);
        if (link is null)
        {
            return Result.Failure(Error.NotFound("File link not found."));
        }

        var asset = await repository.GetAssetAsync(currentUser.TenantId.Value, link.FileId, cancellationToken);
        if (asset is null)
        {
            return Result.Failure(Error.NotFound("File not found."));
        }

        // A comment's attachment belongs to the comment's author.
        if (link.CommentId is { } commentId)
        {
            var comment = await commentRepository.GetByIdAsync(currentUser.TenantId.Value, commentId, cancellationToken);
            if (comment is not null && comment.UserId != currentUser.UserId)
            {
                return Result.Failure(Error.Forbidden("You can only remove files from your own comments."));
            }
        }

        var storageKey = asset.StoragePath;
        repository.Remove(asset, link);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await fileStorage.DeleteAsync(storageKey, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<TaskFileDto>>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<IReadOnlyList<TaskFileDto>>(Error.Unauthorized());
        }

        var links = await repository.ListForTaskAsync(currentUser.TenantId.Value, taskId, cancellationToken);
        return Result.Success<IReadOnlyList<TaskFileDto>>(links.Select(TaskService.ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<TaskFileDto>>> GetBySubTaskIdAsync(Guid subTaskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<IReadOnlyList<TaskFileDto>>(Error.Unauthorized());
        }

        var links = await repository.ListForSubTaskAsync(currentUser.TenantId.Value, subTaskId, cancellationToken);
        return Result.Success<IReadOnlyList<TaskFileDto>>(links.Select(TaskService.ToDto).ToList());
    }
}

// ---------------------------------------------------------------------------
// Notes
// ---------------------------------------------------------------------------

public sealed class NoteService(
    INoteRepository repository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser) : INoteService
{
    public async Task<Result<IReadOnlyList<NoteDto>>> GetMyNotesAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null || currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<NoteDto>>(Error.Unauthorized());
        }

        var notes = await repository.ListForUserAsync(
            currentUser.TenantId.Value, currentUser.UserId.Value, cancellationToken);
        return Result.Success<IReadOnlyList<NoteDto>>(notes.Select(ToDto).ToList());
    }

    public async Task<Result<NoteDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null || currentUser.UserId is null)
        {
            return Result.Failure<NoteDto>(Error.Unauthorized());
        }

        var note = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);

        // Notes are private. An someone else's id is reported as not-found rather than
        // forbidden, so the endpoint cannot be used to probe which ids exist.
        return note is null || note.UserId != currentUser.UserId.Value
            ? Result.Failure<NoteDto>(Error.NotFound("Note not found."))
            : Result.Success(ToDto(note));
    }

    public async Task<Result<NoteDto>> CreateAsync(CreateNoteRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null || currentUser.UserId is null)
        {
            return Result.Failure<NoteDto>(Error.Unauthorized());
        }

        var note = new Note(
            Guid.NewGuid(), currentUser.TenantId.Value, currentUser.UserId.Value,
            request.Title, request.Content, request.Color);
        note.SetPinned(request.IsPinned);

        await repository.AddAsync(note, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(note));
    }

    public async Task<Result<NoteDto>> UpdateAsync(Guid id, UpdateNoteRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null || currentUser.UserId is null)
        {
            return Result.Failure<NoteDto>(Error.Unauthorized());
        }

        var note = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        if (note is null || note.UserId != currentUser.UserId.Value)
        {
            return Result.Failure<NoteDto>(Error.NotFound("Note not found."));
        }

        note.Update(request.Title, request.Content, request.Color);
        note.SetPinned(request.IsPinned);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(note));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null || currentUser.UserId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var note = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        if (note is null || note.UserId != currentUser.UserId.Value)
        {
            return Result.Failure(Error.NotFound("Note not found."));
        }

        repository.Remove(note);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static NoteDto ToDto(Note note) => new(
        note.Id, note.UserId, note.Title, note.Content, note.Color,
        note.IsPinned, note.CreatedAtUtc, note.ModifiedAtUtc);
}

// ---------------------------------------------------------------------------
// Comments
// ---------------------------------------------------------------------------

public sealed class TaskCommentService(
    ITaskCommentRepository repository,
    ITaskRepository taskRepository,
    ITaskFileRepository fileRepository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IFileStorage fileStorage) : ITaskCommentService
{
    public async Task<Result<IReadOnlyList<TaskCommentDto>>> ListAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<IReadOnlyList<TaskCommentDto>>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var comments = await repository.ListForTaskAsync(tenantId, taskId, cancellationToken);
        var users = await taskRepository.GetUserSummariesAsync(
            comments.Select(c => c.UserId).Distinct().ToList(), cancellationToken);
        var files = await fileRepository.ListForCommentsAsync(tenantId, comments.Select(c => c.Id).ToList(), cancellationToken);
        var filesByComment = files.ToLookup(f => f.CommentId!.Value);

        return Result.Success<IReadOnlyList<TaskCommentDto>>(comments
            .Select(c => ToDto(
                c,
                users.TryGetValue(c.UserId, out var u) ? u.DisplayName : null,
                filesByComment[c.Id].Select(TaskService.ToDto).ToList()))
            .ToList());
    }

    public async Task<Result<TaskCommentDto>> CreateAsync(
        Guid taskId, CreateTaskCommentRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null || currentUser.UserId is null)
        {
            return Result.Failure<TaskCommentDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        if (await taskRepository.GetForUpdateAsync(tenantId, taskId, cancellationToken) is null)
        {
            return Result.Failure<TaskCommentDto>(Error.NotFound("Task not found."));
        }

        var comment = new TaskComment(
            Guid.NewGuid(), tenantId, taskId, currentUser.UserId.Value, request.Text);

        await repository.AddAsync(comment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(comment, await DisplayNameAsync(comment.UserId, cancellationToken), []));
    }

    public async Task<Result<TaskCommentDto>> UpdateAsync(
        Guid id, UpdateTaskCommentRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null || currentUser.UserId is null)
        {
            return Result.Failure<TaskCommentDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var comment = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (comment is null)
        {
            return Result.Failure<TaskCommentDto>(Error.NotFound("Comment not found."));
        }

        // Only the author may edit their own words. Forbidden (403), not Unauthorized (401): the
        // caller is signed in, and a 401 would make a client drop its session.
        if (comment.UserId != currentUser.UserId.Value)
        {
            return Result.Failure<TaskCommentDto>(Error.Forbidden("You can only edit your own comments."));
        }

        comment.UpdateText(request.Text);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var files = await fileRepository.ListForCommentsAsync(tenantId, [comment.Id], cancellationToken);
        return Result.Success(ToDto(comment, await DisplayNameAsync(comment.UserId, cancellationToken), files.Select(TaskService.ToDto).ToList()));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null || currentUser.UserId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var comment = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        if (comment is null)
        {
            return Result.Failure(Error.NotFound("Comment not found."));
        }

        if (comment.UserId != currentUser.UserId.Value)
        {
            return Result.Failure(Error.Forbidden("You can only delete your own comments."));
        }

        // The file links point at the comment with NoAction, so they go first; the stored
        // contents are removed once the rows are gone.
        var storageKeys = await fileRepository.ClearForCommentAsync(comment.Id, cancellationToken);
        repository.Remove(comment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var key in storageKeys)
        {
            await fileStorage.DeleteAsync(key, cancellationToken);
        }

        return Result.Success();
    }

    private async Task<string?> DisplayNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        var users = await taskRepository.GetUserSummariesAsync([userId], cancellationToken);
        return users.TryGetValue(userId, out var user) ? user.DisplayName : null;
    }

    private static TaskCommentDto ToDto(TaskComment comment, string? displayName, IReadOnlyList<TaskFileDto> files) =>
        new(comment.Id, comment.TaskId, comment.UserId, displayName,
            comment.Text, comment.CreatedAtUtc, comment.ModifiedAtUtc, files);
}

