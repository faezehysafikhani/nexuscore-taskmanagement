using FluentValidation;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Application.Validators;

/// <summary>
/// Shape checks only. Anything needing the database - does this user exist, does this project
/// still have a subtask - lives in the service, because a validator cannot see the data and
/// splitting the rule across both would let the two drift.
/// </summary>
internal static class RecurrenceRules
{
    public static IRuleBuilderOptions<T, RecurrenceInput> ApplyTo<T>(IRuleBuilderInitial<T, RecurrenceInput> rule) =>
        rule.ChildRules(recurrence =>
        {
            recurrence.RuleFor(r => r.Frequency).IsInEnum();

            recurrence.RuleFor(r => r.EndDate)
                .GreaterThanOrEqualTo(r => r.StartDate)
                .When(r => r.EndDate.HasValue)
                .WithMessage("The recurrence end date cannot be before its start date.");

            recurrence.RuleFor(r => r.EndTime)
                .GreaterThan(r => r.StartTime!.Value)
                .When(r => r.StartTime.HasValue && r.EndTime.HasValue)
                .WithMessage("The end time must be after the start time.");

            // Weekly without a selected day would never fire.
            recurrence.RuleFor(r => r.WeeklyDays)
                .NotEmpty()
                .When(r => r.Frequency == RecurrenceFrequency.Weekly)
                .WithMessage("Select at least one day of the week.");

            recurrence.RuleForEach(r => r.WeeklyDays!)
                .InclusiveBetween(0, 6)
                .When(r => r.WeeklyDays is not null)
                .WithMessage("Days of the week run from 0 (Saturday) to 6 (Friday).");

            recurrence.RuleFor(r => r.IntervalWeeks)
                .InclusiveBetween(1, 52)
                .When(r => r.Frequency == RecurrenceFrequency.Weekly && r.IntervalWeeks.HasValue);

            recurrence.RuleFor(r => r.MonthlyDays)
                .NotEmpty()
                .When(r => r.Frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.MonthlyDay)
                .WithMessage("Select at least one day of the month.");

            recurrence.RuleForEach(r => r.MonthlyDays!)
                .InclusiveBetween(1, 31)
                .When(r => r.MonthlyDays is not null);

            recurrence.RuleFor(r => r.NthOccurrence)
                .NotNull()
                .When(r => r.Frequency == RecurrenceFrequency.MonthlyNthWeekday)
                .WithMessage("Choose which occurrence in the month this falls on.");

            recurrence.RuleFor(r => r.NthWeekday)
                .NotNull()
                .InclusiveBetween(0, 6)
                .When(r => r.Frequency == RecurrenceFrequency.MonthlyNthWeekday)
                .WithMessage("Choose a weekday from 0 (Saturday) to 6 (Friday).");
        });
}

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.CharterDescription).MaximumLength(4000);
        RuleFor(x => x.CharterProjectManager).MaximumLength(200);

        RuleFor(x => x.CharterEndDate)
            .GreaterThanOrEqualTo(x => x.CharterStartDate!.Value)
            .When(x => x.CharterStartDate.HasValue && x.CharterEndDate.HasValue)
            .WithMessage("The charter end date cannot be before its start date.");

        // A project without work under it is not a project.
        RuleFor(x => x.SubTasks)
            .NotEmpty()
            .When(x => x.IsProject)
            .WithMessage("A project must have at least one subtask.");

        RuleForEach(x => x.SubTasks!).ChildRules(subTask =>
        {
            subTask.RuleFor(s => s.Title).NotEmpty().MaximumLength(200);
            subTask.RuleFor(s => s.Importance).IsInEnum();
            subTask.RuleFor(s => s.EndDate)
                .GreaterThanOrEqualTo(s => s.StartDate!.Value)
                .When(s => s.StartDate.HasValue && s.EndDate.HasValue);
        });

        RuleForEach(x => x.Tags!).NotEmpty().MaximumLength(100);

        RecurrenceRules.ApplyTo(RuleFor(x => x.Recurrence!)).When(x => x.Recurrence is not null);
    }
}

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.CharterDescription).MaximumLength(4000);
        RuleFor(x => x.CharterProjectManager).MaximumLength(200);

        RuleFor(x => x.CharterEndDate)
            .GreaterThanOrEqualTo(x => x.CharterStartDate!.Value)
            .When(x => x.CharterStartDate.HasValue && x.CharterEndDate.HasValue);
    }
}

public sealed class ChangeTaskStatusRequestValidator : AbstractValidator<ChangeTaskStatusRequest>
{
    public ChangeTaskStatusRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}

public sealed class ChangeTaskPriorityRequestValidator : AbstractValidator<ChangeTaskPriorityRequest>
{
    public ChangeTaskPriorityRequestValidator() => RuleFor(x => x.Priority).IsInEnum();
}

public sealed class AssignUserRequestValidator : AbstractValidator<AssignUserRequest>
{
    public AssignUserRequestValidator()
    {
        RuleFor(x => x.AssignedUserId).NotEqual(Guid.Empty).When(x => x.AssignedUserId.HasValue);
        RuleForEach(x => x.AssigneeUserIds!).NotEqual(Guid.Empty);
    }
}

public sealed class AssignUserGroupRequestValidator : AbstractValidator<AssignUserGroupRequest>
{
    public AssignUserGroupRequestValidator() =>
        RuleFor(x => x.AssignedUserGroupId).NotEqual(Guid.Empty).When(x => x.AssignedUserGroupId.HasValue);
}

public sealed class CreateSubTaskRequestValidator : AbstractValidator<CreateSubTaskRequest>
{
    public CreateSubTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Importance).IsInEnum();
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue);
    }
}

public sealed class UpdateSubTaskRequestValidator : AbstractValidator<UpdateSubTaskRequest>
{
    public UpdateSubTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Importance).IsInEnum();
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue);
    }
}

public sealed class CreateRepetitiveTaskRequestValidator : AbstractValidator<CreateRepetitiveTaskRequest>
{
    public CreateRepetitiveTaskRequestValidator()
    {
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty);
        RuleFor(x => x.Recurrence).NotNull();
        RecurrenceRules.ApplyTo(RuleFor(x => x.Recurrence));
    }
}

public sealed class UpdateRepetitiveTaskRequestValidator : AbstractValidator<UpdateRepetitiveTaskRequest>
{
    public UpdateRepetitiveTaskRequestValidator()
    {
        RuleFor(x => x.Recurrence).NotNull();
        RecurrenceRules.ApplyTo(RuleFor(x => x.Recurrence));
    }
}

public sealed class CreateTagRequestValidator : AbstractValidator<CreateTagRequest>
{
    public CreateTagRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Color).MaximumLength(30);
    }
}

public sealed class UpdateTagRequestValidator : AbstractValidator<UpdateTagRequest>
{
    public UpdateTagRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Color).MaximumLength(30);
    }
}

public sealed class AssignTagRequestValidator : AbstractValidator<AssignTagRequest>
{
    public AssignTagRequestValidator() => RuleFor(x => x.TagId).NotEqual(Guid.Empty);
}

public sealed class CreateNoteRequestValidator : AbstractValidator<CreateNoteRequest>
{
    public CreateNoteRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotNull();
        RuleFor(x => x.Color).MaximumLength(30);
    }
}

public sealed class UpdateNoteRequestValidator : AbstractValidator<UpdateNoteRequest>
{
    public UpdateNoteRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotNull();
        RuleFor(x => x.Color).MaximumLength(30);
    }
}

public sealed class CreateTaskCommentRequestValidator : AbstractValidator<CreateTaskCommentRequest>
{
    // Text may be empty: a comment can consist of attachments only, uploaded right after it.
    public CreateTaskCommentRequestValidator() =>
        RuleFor(x => x.Text).NotNull().MaximumLength(4000);
}

public sealed class UpdateTaskCommentRequestValidator : AbstractValidator<UpdateTaskCommentRequest>
{
    public UpdateTaskCommentRequestValidator() =>
        RuleFor(x => x.Text).NotNull().MaximumLength(4000);
}

public sealed class UploadFileRequestValidator : AbstractValidator<UploadFileRequest>
{
    public UploadFileRequestValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.ContentType).MaximumLength(150);

        // Checked against the bytes actually received. The service repeats this check, so a
        // caller that bypasses validation still cannot store an oversized file.
        RuleFor(x => x.Content)
            .NotNull()
            .Must(content => content is { Length: > 0 and <= TaskFileAsset.MaxFileSizeBytes })
            .WithMessage($"Files must be between 1 and {TaskFileAsset.MaxFileSizeBytes} bytes (200 KB).");
    }
}
