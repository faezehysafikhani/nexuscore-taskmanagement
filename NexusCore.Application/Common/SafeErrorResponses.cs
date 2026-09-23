using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NexusCore.Application.Common;

/// <summary>
/// The last line of the platform's error handling (next to <see cref="EndpointResults"/>, which
/// turns expected failures into ProblemDetails): an unexpected exception is logged on the server
/// with all its details and answered with the same ProblemDetails shape - a stable code in
/// "title" and a Persian message - never a stack trace, exception text, SQL or server path.
/// </summary>
public static class SafeErrorResponses
{
    public const string ServerErrorCode = "server.error";
    public const string ServerErrorMessage = "خطایی در انجام عملیات رخ داد. لطفاً دوباره تلاش کنید.";
    public const string BadRequestMessage = "اطلاعات ارسال‌شده معتبر نیست.";

    public static IApplicationBuilder UseSafeErrorResponses(this IApplicationBuilder app) =>
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

            // A request the framework could not read (malformed JSON, wrong value types).
            if (exception is BadHttpRequestException badRequest)
            {
                await Results.Problem(BadRequestMessage, statusCode: badRequest.StatusCode, title: "validation.error").ExecuteAsync(context);
                return;
            }

            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("NexusCore.UnhandledException")
                .LogError(exception, "Unhandled exception on {Method} {Path}.", context.Request.Method, context.Request.Path);

            await Results.Problem(ServerErrorMessage, statusCode: StatusCodes.Status500InternalServerError, title: ServerErrorCode).ExecuteAsync(context);
        }));
}
