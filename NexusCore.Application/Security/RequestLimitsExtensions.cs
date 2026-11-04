using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using NexusCore.Application.Files;

namespace NexusCore.Application.Security;

public static class RequestLimitsExtensions
{
    /// <summary>
    /// Explicit size limits instead of the framework defaults: no request body is larger than one
    /// upload (the hard ceiling plus its form), and a multipart body is cut off at the same size.
    /// Upload endpoints add their own, tighter limit (<c>WithUploadSizeLimit</c>).
    /// </summary>
    public static IServiceCollection AddNexusCoreRequestLimits(this IServiceCollection services)
    {
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = UploadValidation.RequestBodyLimitBytes;
            options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
        });
        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = UploadValidation.RequestBodyLimitBytes;
            options.ValueCountLimit = 200;
        });
        return services;
    }
}
