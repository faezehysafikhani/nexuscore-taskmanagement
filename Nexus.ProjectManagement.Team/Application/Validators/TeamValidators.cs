using FluentValidation;
using Nexus.ProjectManagement.Team.Application.Dtos;

namespace Nexus.ProjectManagement.Team.Application.Validators;

public sealed class CreateGovernanceRoleRequestValidator : AbstractValidator<CreateGovernanceRoleRequest>
{
    public CreateGovernanceRoleRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class UpdateGovernanceRoleRequestValidator : AbstractValidator<UpdateGovernanceRoleRequest>
{
    public UpdateGovernanceRoleRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
