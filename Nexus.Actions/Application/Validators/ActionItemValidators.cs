using FluentValidation;
using Nexus.Actions.Application.Dtos;

namespace Nexus.Actions.Application.Validators;

public sealed class CreateActionItemRequestValidator : AbstractValidator<CreateActionItemRequest>
{
    public CreateActionItemRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateActionItemRequestValidator : AbstractValidator<UpdateActionItemRequest>
{
    public UpdateActionItemRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}
