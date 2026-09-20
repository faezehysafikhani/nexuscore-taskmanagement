using FluentValidation;
using Nexus.Workflow.Application.Dtos;

namespace Nexus.Workflow.Application.Validators;

public sealed class CreateWorkflowDefinitionRequestValidator : AbstractValidator<CreateWorkflowDefinitionRequest>
{
    public CreateWorkflowDefinitionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.SubjectType).NotEmpty().MaximumLength(80);
    }
}

public sealed class AddWorkflowStepRequestValidator : AbstractValidator<AddWorkflowStepRequest>
{
    public AddWorkflowStepRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
    }
}
