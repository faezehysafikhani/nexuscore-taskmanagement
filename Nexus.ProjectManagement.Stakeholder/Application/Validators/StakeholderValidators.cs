using FluentValidation;
using Nexus.ProjectManagement.StakeholderManagement.Application.Dtos;

namespace Nexus.ProjectManagement.StakeholderManagement.Application.Validators;

public sealed class CreateStakeholderRequestValidator : AbstractValidator<CreateStakeholderRequest>
{
    public CreateStakeholderRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateStakeholderRequestValidator : AbstractValidator<UpdateStakeholderRequest>
{
    public UpdateStakeholderRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
