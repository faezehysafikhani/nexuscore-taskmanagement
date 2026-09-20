using FluentValidation;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;

namespace Nexus.ProjectManagement.Waterfall.Application.Validators;

public sealed class CreateActivityRequestValidator : AbstractValidator<CreateActivityRequest>
{
    public CreateActivityRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Weight).InclusiveBetween(0, 100);
    }
}

public sealed class UpdateActivityRequestValidator : AbstractValidator<UpdateActivityRequest>
{
    public UpdateActivityRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Weight).InclusiveBetween(0, 100);
    }
}

public sealed class UpdateActivityProgressRequestValidator : AbstractValidator<UpdateActivityProgressRequest>
{
    public UpdateActivityProgressRequestValidator()
    {
        RuleFor(x => x.PlannedProgress).InclusiveBetween(0, 100);
        RuleFor(x => x.ActualProgress).InclusiveBetween(0, 100);
    }
}
