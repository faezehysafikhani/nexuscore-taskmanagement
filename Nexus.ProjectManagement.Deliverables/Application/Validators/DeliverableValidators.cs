using FluentValidation;
using Nexus.ProjectManagement.Deliverables.Application.Dtos;

namespace Nexus.ProjectManagement.Deliverables.Application.Validators;

public sealed class CreateDeliverableRequestValidator : AbstractValidator<CreateDeliverableRequest>
{
    public CreateDeliverableRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateDeliverableRequestValidator : AbstractValidator<UpdateDeliverableRequest>
{
    public UpdateDeliverableRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}
