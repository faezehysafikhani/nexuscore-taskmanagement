using FluentValidation;
using Nexus.Actions.Application.Dtos;

namespace Nexus.Actions.Application.Validators;

public sealed class CreateActionItemRequestValidator : AbstractValidator<CreateActionItemRequest>
{
    public CreateActionItemRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Priority).IsInEnum().When(x => x.Priority.HasValue);
    }
}

public sealed class UpdateActionItemRequestValidator : AbstractValidator<UpdateActionItemRequest>
{
    public UpdateActionItemRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Priority).IsInEnum().When(x => x.Priority.HasValue);
    }
}
