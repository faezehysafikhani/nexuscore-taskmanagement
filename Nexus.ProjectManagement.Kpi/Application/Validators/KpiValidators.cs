using FluentValidation;
using Nexus.ProjectManagement.Kpi.Application.Dtos;

namespace Nexus.ProjectManagement.Kpi.Application.Validators;

public sealed class CreateKpiDefinitionRequestValidator : AbstractValidator<CreateKpiDefinitionRequest>
{
    public CreateKpiDefinitionRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
    }
}

public sealed class UpdateKpiDefinitionRequestValidator : AbstractValidator<UpdateKpiDefinitionRequest>
{
    public UpdateKpiDefinitionRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
    }
}
