namespace NexusCore.Application.Files;

public sealed record StoredFile(string StorageKey, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// Common infrastructure (rule: stays in NexusCore, not duplicated per module). Documents and
/// Knowledge both store file bytes through this same abstraction and keep only the returned
/// StorageKey plus their own metadata (title, type, description, ...) in their own tables.
/// </summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
