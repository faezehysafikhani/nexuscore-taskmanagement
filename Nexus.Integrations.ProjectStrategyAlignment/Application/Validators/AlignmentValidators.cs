using FluentValidation;
using Nexus.Integrations.StrategyAlignment.Application.Dtos;

namespace Nexus.Integrations.StrategyAlignment.Application.Validators;

public sealed class CreateAlignmentRequestValidator : AbstractValidator<CreateAlignmentRequest>
{
    public CreateAlignmentRequestValidator()
    {
        RuleFor(x => x.AlignmentPercentage).InclusiveBetween(0, 100).When(x => x.AlignmentPercentage is not null);
    }
}

public sealed class UpdateAlignmentRequestValidator : AbstractValidator<UpdateAlignmentRequest>
{
    public UpdateAlignmentRequestValidator()
    {
        RuleFor(x => x.AlignmentPercentage).InclusiveBetween(0, 100).When(x => x.AlignmentPercentage is not null);
    }
}
