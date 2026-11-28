using Microsoft.AspNetCore.Http;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Common;

/// <summary>
/// Shared Result -&gt; IResult mapping so every module's endpoint-mapping code (which lives in
/// each module's own project, not a separate Api-tier project) can use the same conventions
/// as NexusCore.Api without duplicating this logic.
/// </summary>
public static class EndpointResults
{
    public static IResult ToApiResult(this Result result)
    {
        if (result.IsSuccess)
        {
            return Results.NoContent();
        }

        return ToProblem(result.Error);
    }

    public static IResult ToApiResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        return ToProblem(result.Error);
    }

    private static IResult ToProblem(Error error)
    {
        // A session that is over (idle, absolute, revoked ...): the client signs the user out and says why.
        var statusCode = error.Code.StartsWith("session.", StringComparison.Ordinal) ? StatusCodes.Status401Unauthorized : error.Code switch
        {
            "validation.error" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            // Someone else changed it meanwhile (or is changing it now): read again and retry.
            "conflict.changed" => StatusCodes.Status409Conflict,
            // Two decisions on the same approval step at once: the second is told to read again.
            "workflow.concurrent_decision" => StatusCodes.Status409Conflict,
            // The submitter tried to decide their own request.
            "workflow.self_approval" => StatusCodes.Status403Forbidden,
            // A project in a state that does not allow the request (pending approval, not yet approved, already approved).
            "project.pending_approval" => StatusCodes.Status409Conflict,
            "project.already_pending" => StatusCodes.Status409Conflict,
            "project.already_approved" => StatusCodes.Status409Conflict,
            "project.not_approved" => StatusCodes.Status409Conflict,
            "project.forbidden" => StatusCodes.Status403Forbidden,
            "unauthorized" => StatusCodes.Status401Unauthorized,
            // A failed sign-in after which the next attempt must include a CAPTCHA.
            "unauthorized.captcha_required" => StatusCodes.Status401Unauthorized,
            "too_many_requests" => StatusCodes.Status429TooManyRequests,
            "forbidden" => StatusCodes.Status403Forbidden,
            // The right password for a disabled account (IdentityService.AccountDisabled).
            "account.disabled" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Problem(error.Message, statusCode: statusCode, title: error.Code);
    }
}
