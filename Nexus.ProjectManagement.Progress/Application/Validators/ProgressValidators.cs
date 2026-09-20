using FluentValidation;
using Nexus.ProjectManagement.Progress.Application.Dtos;

namespace Nexus.ProjectManagement.Progress.Application.Validators;

public sealed class CreateProgressUpdateRequestValidator : AbstractValidator<CreateProgressUpdateRequest>
{
    public CreateProgressUpdateRequestValidator()
    {
        RuleFor(x => x.PlannedProgress).InclusiveBetween(0, 100);
        RuleFor(x => x.ActualProgress).InclusiveBetween(0, 100);
    }
}

public sealed class UpdateProgressUpdateRequestValidator : AbstractValidator<UpdateProgressUpdateRequest>
{
    public UpdateProgressUpdateRequestValidator()
    {
        RuleFor(x => x.PlannedProgress).InclusiveBetween(0, 100);
        RuleFor(x => x.ActualProgress).InclusiveBetween(0, 100);
    }
}
