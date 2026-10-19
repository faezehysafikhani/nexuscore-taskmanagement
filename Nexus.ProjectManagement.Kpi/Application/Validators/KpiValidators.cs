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

public sealed class RecordKpiMeasurementRequestValidator : AbstractValidator<RecordKpiMeasurementRequest>
{
    public RecordKpiMeasurementRequestValidator()
    {
        RuleFor(x => x.MeasuredOn).Must(KpiMeasurementRules.IsPlausibleDate).WithMessage("The measurement date is missing or in the future.");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class UpdateKpiMeasurementRequestValidator : AbstractValidator<UpdateKpiMeasurementRequest>
{
    public UpdateKpiMeasurementRequestValidator()
    {
        RuleFor(x => x.MeasuredOn).Must(KpiMeasurementRules.IsPlausibleDate).WithMessage("The measurement date is missing or in the future.");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

internal static class KpiMeasurementRules
{
    /// <summary>A value is measured on a day that has come; one day of slack covers the time-zone difference with the server.</summary>
    public static bool IsPlausibleDate(DateOnly date) =>
        date != default && date <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
}
