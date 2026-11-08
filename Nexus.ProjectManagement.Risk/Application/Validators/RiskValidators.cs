using FluentValidation;
using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Application.Validators;

public sealed class CreateRiskRequestValidator : AbstractValidator<CreateRiskRequest>
{
    public CreateRiskRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.ProbabilityScore).InclusiveBetween(1, 5).When(x => x.ProbabilityLevel is null);
        RuleFor(x => x.SeverityScore).InclusiveBetween(1, 5).When(x => x.ImpactLevel is null);
        RuleFor(x => x.ImpactScore).InclusiveBetween(1, 5).When(x => x.ImpactLevel is null);
        RuleFor(x => x.ProbabilityLevel).Must(RiskLevels.IsLevel).When(x => x.ProbabilityLevel is not null).WithMessage("Probability level must be low, medium or high.");
        RuleFor(x => x.ImpactLevel).Must(RiskLevels.IsLevel).When(x => x.ImpactLevel is not null).WithMessage("Impact level must be low, medium or high.");
    }
}

public sealed class UpdateRiskRequestValidator : AbstractValidator<UpdateRiskRequest>
{
    public UpdateRiskRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.ProbabilityScore).InclusiveBetween(1, 5).When(x => x.ProbabilityLevel is null);
        RuleFor(x => x.SeverityScore).InclusiveBetween(1, 5).When(x => x.ImpactLevel is null);
        RuleFor(x => x.ImpactScore).InclusiveBetween(1, 5).When(x => x.ImpactLevel is null);
        RuleFor(x => x.ProbabilityLevel).Must(RiskLevels.IsLevel).When(x => x.ProbabilityLevel is not null).WithMessage("Probability level must be low, medium or high.");
        RuleFor(x => x.ImpactLevel).Must(RiskLevels.IsLevel).When(x => x.ImpactLevel is not null).WithMessage("Impact level must be low, medium or high.");
    }
}
