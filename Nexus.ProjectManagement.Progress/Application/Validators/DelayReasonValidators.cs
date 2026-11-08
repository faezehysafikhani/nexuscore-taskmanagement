using FluentValidation;
using Nexus.ProjectManagement.Progress.Application.Dtos;

namespace Nexus.ProjectManagement.Progress.Application.Validators;

public sealed class CreateDelayReasonRequestValidator : AbstractValidator<CreateDelayReasonRequest>
{
    public CreateDelayReasonRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.RootCause).IsInEnum();
        RuleFor(x => x.TimeImpactDays).GreaterThanOrEqualTo(0).When(x => x.TimeImpactDays.HasValue);
        RuleFor(x => x.CostImpact).GreaterThanOrEqualTo(0).When(x => x.CostImpact.HasValue);
        RuleFor(x => x.CorrectiveAction).MaximumLength(2000);
    }
}

public sealed class UpdateDelayReasonRequestValidator : AbstractValidator<UpdateDelayReasonRequest>
{
    public UpdateDelayReasonRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.RootCause).IsInEnum();
        RuleFor(x => x.TimeImpactDays).GreaterThanOrEqualTo(0).When(x => x.TimeImpactDays.HasValue);
        RuleFor(x => x.CostImpact).GreaterThanOrEqualTo(0).When(x => x.CostImpact.HasValue);
        RuleFor(x => x.CorrectiveAction).MaximumLength(2000);
    }
}
