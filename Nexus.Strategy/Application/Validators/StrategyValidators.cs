using FluentValidation;
using Nexus.StrategyManagement.Application.Dtos;

namespace Nexus.StrategyManagement.Application.Validators;

public sealed class CreateStrategyRequestValidator : AbstractValidator<CreateStrategyRequest>
{
    public CreateStrategyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Weight).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateStrategyRequestValidator : AbstractValidator<UpdateStrategyRequest>
{
    public UpdateStrategyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Weight).GreaterThanOrEqualTo(0);
    }
}
