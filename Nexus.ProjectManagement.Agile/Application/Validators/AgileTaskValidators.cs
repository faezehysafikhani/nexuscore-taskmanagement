using FluentValidation;
using Nexus.ProjectManagement.Agile.Application.Dtos;

namespace Nexus.ProjectManagement.Agile.Application.Validators;

public sealed class CreateAgileTaskRequestValidator : AbstractValidator<CreateAgileTaskRequest>
{
    public CreateAgileTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateAgileTaskRequestValidator : AbstractValidator<UpdateAgileTaskRequest>
{
    public UpdateAgileTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}
