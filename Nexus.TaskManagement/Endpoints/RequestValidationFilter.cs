using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Endpoints;

/// <summary>
/// Runs the registered FluentValidation validator for every request argument that has one,
/// before the handler. Invalid input gets the platform's usual 400 (validation.error) instead of
/// reaching the domain or the database.
///
/// The module's validators were registered but nothing invoked them; this filter is the single
/// place that does, so a new endpoint is covered as soon as its request type has a validator.
/// </summary>
public sealed class RequestValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument),
                context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                var message = string.Join("; ", result.Errors.Select(error => error.ErrorMessage));
                return Result.Failure(Error.Validation(message)).ToApiResult();
            }
        }

        return await next(context);
    }
}
