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
        // Named by a new id and a known extension only: nothing the client typed is in the path.
        var storageKey = $"{Guid.NewGuid():N}{AllowedUploadTypes.StorageExtensionFor(fileName)}";
        var path = Path.Combine(_rootPath, storageKey);

        // The bytes are counted as they are copied, whatever size the request claimed.
        const long ceiling = UploadPolicySettings.HardCeilingKb * 1024L;
        long sizeBytes = 0;
        try
        {
            await using (var fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    sizeBytes += read;
                    if (sizeBytes > ceiling)
                    {
                        throw new InvalidDataException($"The file is larger than the {ceiling}-byte limit.");
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
        }
        catch
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            throw;
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
