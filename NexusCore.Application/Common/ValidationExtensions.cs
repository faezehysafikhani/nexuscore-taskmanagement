using FluentValidation;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Common;

public static class ValidationExtensions
{
    public static async Task<Result> ValidateAsResultAsync<T>(this IValidator<T> validator, T request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (validation.IsValid)
        {
            return Result.Success();
        }

        var message = string.Join("; ", validation.Errors.Select(error => error.ErrorMessage));
        return Result.Failure(Error.Validation(message));
    }
}
