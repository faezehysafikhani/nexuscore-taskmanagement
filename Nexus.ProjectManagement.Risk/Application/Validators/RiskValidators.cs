using FluentValidation;
using Nexus.ProjectManagement.RiskManagement.Application.Dtos;

namespace Nexus.ProjectManagement.RiskManagement.Application.Validators;

public sealed class CreateRiskRequestValidator : AbstractValidator<CreateRiskRequest>
{
    public CreateRiskRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.ProbabilityScore).InclusiveBetween(1, 5);
        RuleFor(x => x.SeverityScore).InclusiveBetween(1, 5);
        RuleFor(x => x.ImpactScore).InclusiveBetween(1, 5);
    }
}

public sealed class UpdateRiskRequestValidator : AbstractValidator<UpdateRiskRequest>
{
    public UpdateRiskRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.ProbabilityScore).InclusiveBetween(1, 5);
        RuleFor(x => x.SeverityScore).InclusiveBetween(1, 5);
        RuleFor(x => x.ImpactScore).InclusiveBetween(1, 5);
    }
}
