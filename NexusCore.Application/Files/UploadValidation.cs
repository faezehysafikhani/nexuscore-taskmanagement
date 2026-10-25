using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Files;

/// <summary>
/// The one set of rules every upload goes through, for modules that receive a stream: an
/// allowed type by extension, the first bytes agreeing with it, and the configured size limit.
/// What is stored and served afterwards carries the type of the extension
/// (<see cref="AllowedUploadTypes.ContentTypeFor"/>), never the one the client sent.
/// </summary>
public static class UploadValidation
{
    /// <summary>The request body limit for one upload: the hard ceiling plus room for the form around the file.</summary>
    public const long RequestBodyLimitBytes = UploadPolicySettings.HardCeilingKb * 1024L + 1024L * 1024L;

    /// <summary>Checks a seekable stream; on success gives the clean display name and the type to store.</summary>
    public static async Task<Result<(string FileName, string ContentType)>> CheckAsync(
        IUploadPolicyReader policy, Guid tenantId, string? fileName, Stream content, CancellationToken cancellationToken)
    {
        var displayName = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Failure<(string, string)>(Error.Validation("The file name is required."));
        }

        if (!content.CanSeek || content.Length == 0)
        {
            return Result.Failure<(string, string)>(Error.Validation("The uploaded file is empty."));
        }

        if (!AllowedUploadTypes.IsAllowed(displayName, null))
        {
            return Result.Failure<(string, string)>(Error.Validation("This file type is not allowed. Only Excel, Word, PDF and image files can be uploaded."));
        }

        var maxBytes = await policy.GetMaxFileSizeKbAsync(tenantId, cancellationToken) * 1024L;
        if (content.Length > maxBytes)
        {
            return Result.Failure<(string, string)>(Error.Validation($"File is too large. The maximum size is {maxBytes} bytes."));
        }

        if (!await AllowedUploadTypes.StreamMatchesAsync(displayName, content, cancellationToken))
        {
            return Result.Failure<(string, string)>(Error.Validation("The file content does not match its type. Upload a genuine Excel, Word, PDF or image file."));
        }

        return Result.Success((displayName, AllowedUploadTypes.ContentTypeFor(displayName)));
    }
}

public static class UploadEndpointExtensions
{
    /// <summary>Caps the request body of an upload endpoint, before any of it is buffered.</summary>
    public static TBuilder WithUploadSizeLimit<TBuilder>(this TBuilder builder) where TBuilder : Microsoft.AspNetCore.Builder.IEndpointConventionBuilder =>
        builder.WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(UploadValidation.RequestBodyLimitBytes));
}
