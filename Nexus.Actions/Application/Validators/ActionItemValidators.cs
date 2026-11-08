using FluentValidation;
using Nexus.Actions.Application.Dtos;

namespace Nexus.Actions.Application.Validators;

public sealed class CreateActionItemRequestValidator : AbstractValidator<CreateActionItemRequest>
{
    public CreateActionItemRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Priority).IsInEnum().When(x => x.Priority.HasValue);
        RuleFor(x => x.Recurrence!.Unit).IsInEnum().When(x => x.Recurrence?.Unit is not null);
        RuleFor(x => x.Recurrence!.Interval).InclusiveBetween(1, 365).When(x => x.Recurrence?.Unit is not null);
    }
}

public sealed class UpdateActionItemRequestValidator : AbstractValidator<UpdateActionItemRequest>
{
    public UpdateActionItemRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Priority).IsInEnum().When(x => x.Priority.HasValue);
        RuleFor(x => x.Recurrence!.Unit).IsInEnum().When(x => x.Recurrence?.Unit is not null);
        RuleFor(x => x.Recurrence!.Interval).InclusiveBetween(1, 365).When(x => x.Recurrence?.Unit is not null);
    }
}
