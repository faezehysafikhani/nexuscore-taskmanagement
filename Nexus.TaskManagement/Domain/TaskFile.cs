using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>
/// An uploaded file. Never stored under the name the client supplied - the service generates
/// <see cref="StoredFileName"/> - and never larger than <see cref="MaxFileSizeBytes"/>, which
/// is enforced in the service against the real stream length and again by a CHECK constraint.
/// </summary>
public sealed class TaskFileAsset : AuditableEntity<Guid>
{
    /// <summary>200 KB.</summary>
    public const int MaxFileSizeBytes = 204_800;

    private TaskFileAsset() : base(Guid.Empty)
    {
        OriginalFileName = string.Empty;
        StoredFileName = string.Empty;
        ContentType = string.Empty;
        StoragePath = string.Empty;
    }

    public TaskFileAsset(
        Guid id,
        Guid tenantId,
        string originalFileName,
        string storedFileName,
        string contentType,
        int fileSizeBytes,
        string storagePath,
        Guid? uploadedByUserId) : base(id)
    {
        if (fileSizeBytes <= 0 || fileSizeBytes > MaxFileSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileSizeBytes),
                fileSizeBytes,
                $"File size must be between 1 and {MaxFileSizeBytes} bytes.");
        }

        TenantId = tenantId;
        OriginalFileName = originalFileName.Trim();
        StoredFileName = storedFileName.Trim();
        ContentType = contentType.Trim();
        FileSizeBytes = fileSizeBytes;
        StoragePath = storagePath.Trim();
        UploadedByUserId = uploadedByUserId;
    }

    public Guid TenantId { get; private set; }
    public string OriginalFileName { get; private set; }
    public string StoredFileName { get; private set; }
    public string ContentType { get; private set; }
    public int FileSizeBytes { get; private set; }
    public string StoragePath { get; private set; }

    public Guid? UploadedByUserId { get; private set; }
    public User? UploadedByUser { get; private set; }
}

/// <summary>
/// Links a file to exactly one owner: a task, a subtask or a comment. Recurring tasks live in
/// the Tasks table like everything else, so their files hang off TaskId - there is no separate
/// column for them.
///
/// Exactly one of TaskId / SubTaskId / CommentId is set; a CHECK constraint enforces it.
/// </summary>
public sealed class TaskFile : Entity<Guid>
{
    private TaskFile() : base(Guid.Empty)
    {
    }

    private TaskFile(Guid id, Guid fileId, Guid? taskId, Guid? subTaskId, Guid? commentId = null) : base(id)
    {
        FileId = fileId;
        TaskId = taskId;
        SubTaskId = subTaskId;
        CommentId = commentId;
    }

    public Guid FileId { get; private set; }
    public TaskFileAsset? File { get; private set; }

    public Guid? TaskId { get; private set; }
    public TaskItem? Task { get; private set; }

    public Guid? SubTaskId { get; private set; }
    public SubTask? SubTask { get; private set; }

    public Guid? CommentId { get; private set; }
    public TaskComment? Comment { get; private set; }

    public static TaskFile ForTask(Guid id, Guid fileId, Guid taskId) => new(id, fileId, taskId, null);

    public static TaskFile ForSubTask(Guid id, Guid fileId, Guid subTaskId) => new(id, fileId, null, subTaskId);

    public static TaskFile ForComment(Guid id, Guid fileId, Guid commentId) => new(id, fileId, null, null, commentId);
}
