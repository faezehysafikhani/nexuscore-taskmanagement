using FluentValidation;
using Nexus.ProjectManagement.Agile.Application.Dtos;

namespace Nexus.ProjectManagement.Agile.Application.Validators;

public sealed class CreateAgileTaskRequestValidator : AbstractValidator<CreateAgileTaskRequest>
{
    public CreateAgileTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StoryPoints).InclusiveBetween(0, AgileEstimates.MaxStoryPoints).When(x => x.StoryPoints.HasValue);
    }
}

public sealed class UpdateAgileTaskRequestValidator : AbstractValidator<UpdateAgileTaskRequest>
{
    public UpdateAgileTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StoryPoints).InclusiveBetween(0, AgileEstimates.MaxStoryPoints).When(x => x.StoryPoints.HasValue);
    }
}

public static class AgileEstimates
{
    /// <summary>No real task is a thousand points; this stops a typo from skewing every chart.</summary>
    public const int MaxStoryPoints = 1000;
}

public sealed class SetStoryPointsRequestValidator : AbstractValidator<SetStoryPointsRequest>
{
    public SetStoryPointsRequestValidator()
    {
        RuleFor(x => x.StoryPoints).InclusiveBetween(0, AgileEstimates.MaxStoryPoints).When(x => x.StoryPoints.HasValue);
    }
}

public sealed class CreateSprintRequestValidator : AbstractValidator<CreateSprintRequest>
{
    public CreateSprintRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Goal).MaximumLength(1000);
    }
}

public sealed class UpdateSprintRequestValidator : AbstractValidator<UpdateSprintRequest>
{
    public UpdateSprintRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Goal).MaximumLength(1000);
    }
}

public sealed class CreateChecklistItemRequestValidator : AbstractValidator<CreateChecklistItemRequest>
{
    public CreateChecklistItemRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(500);
    }
}

public sealed class UpdateChecklistItemRequestValidator : AbstractValidator<UpdateChecklistItemRequest>
{
    public UpdateChecklistItemRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(500);
    }
}
