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

    /// <summary>
    /// What was wrong with a body the framework could not read, in Persian and without internals:
    /// a field it does not know, a field it needs, a value of the wrong kind, or not a JSON object.
    /// </summary>
    public static string DescribeBadBody(BadHttpRequestException badRequest)
    {
        if (badRequest.InnerException is not System.Text.Json.JsonException json)
        {
            return BadRequestMessage;
        }

        var message = json.Message;
        var unknown = System.Text.RegularExpressions.Regex.Match(message, @"The JSON property '([^']{1,60})' could not be mapped");
        if (unknown.Success)
        {
            return $"{BadRequestMessage} فیلد «{unknown.Groups[1].Value}» شناخته نیست.";
        }

        var missing = System.Text.RegularExpressions.Regex.Match(message, @"missing required properties[^:]*: ([A-Za-z0-9_, ]{1,200})");
        if (missing.Success)
        {
            return $"{BadRequestMessage} این فیلد لازم است: «{missing.Groups[1].Value.Trim().TrimEnd('.')}».";
        }

        var path = json.Path;
        if (string.IsNullOrEmpty(path) || path == "$")
        {
            return $"{BadRequestMessage} بدنه‌ی درخواست باید یک شیء JSON با فیلدهای همین درخواست باشد.";
        }

        var field = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path;
        return field.Length <= 80
            ? $"{BadRequestMessage} مقدار فیلد «{field}» درست نیست."
            : BadRequestMessage;
    }

    public static IApplicationBuilder UseSafeErrorResponses(this IApplicationBuilder app) =>
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

            // A request the framework could not read (malformed JSON, wrong value types).
            if (exception is BadHttpRequestException badRequest)
            {
                await Results.Problem(DescribeBadBody(badRequest), statusCode: badRequest.StatusCode, title: "validation.error").ExecuteAsync(context);
                return;
            }

            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("NexusCore.UnhandledException")
                .LogError(exception, "Unhandled exception on {Method} {Path}.", context.Request.Method, context.Request.Path);

            await Results.Problem(ServerErrorMessage, statusCode: StatusCodes.Status500InternalServerError, title: ServerErrorCode).ExecuteAsync(context);
        }));
}
