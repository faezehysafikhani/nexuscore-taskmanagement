using Microsoft.Extensions.Configuration;
using NexusCore.Application.Files;

namespace NexusCore.Infrastructure.Files;

/// <summary>
/// Default IFileStorage implementation: files on local disk under FileStorage:RootPath (falls
/// back to a "file-storage" folder next to the running app). Good enough as the platform
/// default; a deployment that needs blob storage can register a different IFileStorage without
/// Documents/Knowledge changing at all.
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalDiskFileStorage(IConfiguration configuration)
    {
        _rootPath = configuration["FileStorage:RootPath"] ?? Path.Combine(AppContext.BaseDirectory, "file-storage");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StoredFile> SaveAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var storageKey = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var path = Path.Combine(_rootPath, storageKey);

        long sizeBytes;
        await using (var fileStream = File.Create(path))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
            sizeBytes = fileStream.Length;
        }

        return new StoredFile(storageKey, fileName, contentType, sizeBytes);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_rootPath, storageKey);
        return Task.FromResult(File.Exists(path) ? (Stream)File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_rootPath, storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
