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
    /// <summary>Most people one task can be the responsibility of.</summary>
    public const int MaxResponsible = 50;

    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.CharterDescription).MaximumLength(4000);
        RuleFor(x => x.CharterProjectManager).MaximumLength(200);

        // Every new task has someone responsible for doing it (the creator may pick themselves) -
        // one or more in ResponsibleUserIds, or the single AssignedUserId older clients send.
        // Existing tasks without one stay valid: this applies to creation only. Where tasks are
        // given to posts, ResponsiblePostIds names them instead (checked by the service).
        RuleFor(x => x.AssignedUserId)
            .NotNull().WithMessage("Choose who is responsible for the task.")
            .NotEqual(Guid.Empty).WithMessage("Choose who is responsible for the task.")
            .When(x => x.ResponsibleUserIds is not { Count: > 0 } && x.ResponsiblePostIds is not { Count: > 0 });
        RuleForEach(x => x.ResponsibleUserIds!).NotEqual(Guid.Empty).WithMessage("Choose who is responsible for the task.");
        RuleFor(x => x.ResponsibleUserIds!.Count).LessThanOrEqualTo(MaxResponsible)
            .When(x => x.ResponsibleUserIds is not null)
            .WithMessage($"A task can have at most {MaxResponsible} responsible people.");

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
            subTask.RuleFor(s => s.AssigneeUserIds)
                .Must(ids => ids is { Count: > 0 })
                .WithMessage("Choose at least one responsible person for the subtask.");
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

        RuleForEach(x => x.ResponsibleUserIds!).NotEqual(Guid.Empty);
        RuleFor(x => x.ResponsibleUserIds!.Count).LessThanOrEqualTo(CreateTaskRequestValidator.MaxResponsible)
            .When(x => x.ResponsibleUserIds is not null);
    }
}

public sealed class ChangeTaskStatusRequestValidator : AbstractValidator<ChangeTaskStatusRequest>
{
    public ChangeTaskStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status is not null);
        RuleFor(x => x).Must(x => (x.Status is null) != (x.CustomStatusId is null))
            .WithMessage("Send either Status or CustomStatusId.");
    }
}

public sealed class SaveRequestCategoryRequestValidator : AbstractValidator<SaveRequestCategoryRequest>
{
    public SaveRequestCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DefaultPriority).IsInEnum();
    }
}

public sealed class SubmitTaskRequestRequestValidator : AbstractValidator<SubmitTaskRequestRequest>
{
    public SubmitTaskRequestRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Priority).IsInEnum().When(x => x.Priority is not null);
    }
}

public sealed class RejectTaskRequestRequestValidator : AbstractValidator<RejectTaskRequestRequest>
{
    public RejectTaskRequestRequestValidator() => RuleFor(x => x.Note).MaximumLength(1000);
}

public sealed class TaskFromMessageRequestValidator : AbstractValidator<TaskFromMessageRequest>
{
    public TaskFromMessageRequestValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty();
        RuleFor(x => x.Text).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Title).MaximumLength(200);
    }
}

public sealed class ImportTasksRequestValidator : AbstractValidator<ImportTasksRequest>
{
    public ImportTasksRequestValidator() => RuleFor(x => x.Csv).NotEmpty();
}

public sealed class SaveReportSubscriptionRequestValidator : AbstractValidator<SaveReportSubscriptionRequest>
{
    public SaveReportSubscriptionRequestValidator() => RuleFor(x => x.Frequency).IsInEnum();
}

public sealed class SetMembershipRequestValidator : AbstractValidator<SetMembershipRequest>
{
    public SetMembershipRequestValidator()
    {
        RuleFor(x => x.PlanName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class SaveWebhookRequestValidator : AbstractValidator<SaveWebhookRequest>
{
    public SaveWebhookRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Url).NotEmpty().MaximumLength(500);
    }
}

public sealed class SaveAutomationRuleRequestValidator : AbstractValidator<SaveAutomationRuleRequest>
{
    public SaveAutomationRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Trigger).IsInEnum();
        RuleFor(x => x.Actions).NotNull();
    }
}

public sealed class LogTimeRequestValidator : AbstractValidator<LogTimeRequest>
{
    public LogTimeRequestValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class UpdateTimeEntryRequestValidator : AbstractValidator<UpdateTimeEntryRequest>
{
    public UpdateTimeEntryRequestValidator() => RuleFor(x => x.Note).MaximumLength(500);
}

public sealed class SaveCriterionRequestValidator : AbstractValidator<SaveCriterionRequest>
{
    public SaveCriterionRequestValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(300);
}

public sealed class SetTaskBlockerRequestValidator : AbstractValidator<SetTaskBlockerRequest>
{
    public SetTaskBlockerRequestValidator() => RuleFor(x => x.Reason).MaximumLength(300);
}

public sealed class BulkSubTasksRequestValidator : AbstractValidator<BulkSubTasksRequest>
{
    public BulkSubTasksRequestValidator()
    {
        RuleFor(x => x.Titles).NotNull();
        RuleForEach(x => x.Titles).MaximumLength(200);
    }
}

public sealed class SaveTaskTemplateRequestValidator : AbstractValidator<SaveTaskTemplateRequest>
{
    public SaveTaskTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Body).NotNull();
        RuleFor(x => x.Body.Title).NotEmpty().MaximumLength(200).When(x => x.Body is not null);
        RuleFor(x => x.Body.Priority).IsInEnum().When(x => x.Body is not null);
    }
}

public sealed class SaveTaskAsTemplateRequestValidator : AbstractValidator<SaveTaskAsTemplateRequest>
{
    public SaveTaskAsTemplateRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public sealed class SubmitTaskReviewRequestValidator : AbstractValidator<SubmitTaskReviewRequest>
{
    public SubmitTaskReviewRequestValidator() => RuleFor(x => x.Comment).MaximumLength(1000);
}

public sealed class QuickCreateTaskRequestValidator : AbstractValidator<QuickCreateTaskRequest>
{
    public QuickCreateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Priority).IsInEnum();
    }
}

public sealed class BulkTaskRequestValidator : AbstractValidator<BulkTaskRequest>
{
    public BulkTaskRequestValidator()
    {
        RuleFor(x => x.Ids).NotNull();
        RuleFor(x => x.Action).IsInEnum();
    }
}

public sealed class CreateTaskStatusRequestValidator : AbstractValidator<CreateTaskStatusRequest>
{
    public CreateTaskStatusRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Color).MaximumLength(30);
    }
}

public sealed class UpdateTaskStatusRequestValidator : AbstractValidator<UpdateTaskStatusRequest>
{
    public UpdateTaskStatusRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Color).MaximumLength(30);
    }
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
        RuleForEach(x => x.ResponsibleUserIds!).NotEqual(Guid.Empty);
        RuleFor(x => x.ResponsibleUserIds!.Count).LessThanOrEqualTo(CreateTaskRequestValidator.MaxResponsible)
            .When(x => x.ResponsibleUserIds is not null);
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
        RuleFor(x => x.AssigneeUserIds)
            .Must(ids => ids is { Count: > 0 })
            .WithMessage("Choose at least one responsible person for the subtask.");
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
        RuleFor(x => x.AssigneeUserIds)
            .Must(ids => ids is { Count: > 0 })
            .WithMessage("Choose at least one responsible person for the subtask.");
    }
}

public sealed class ChangeSubTaskStatusRequestValidator : AbstractValidator<ChangeSubTaskStatusRequest>
{
    public ChangeSubTaskStatusRequestValidator() => RuleFor(x => x.Status).IsInEnum();
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

        // The hard ceiling only (see TaskFileAsset.MaxFileSizeBytes remarks) - the service checks
        // the admin-configured, day-to-day limit separately, since that value can change at
        // runtime and this validator cannot depend on it.
        RuleFor(x => x.Content)
            .NotNull()
            .Must(content => content is { Length: > 0 and <= TaskFileAsset.MaxFileSizeBytes })
            .WithMessage($"Files must be between 1 and {TaskFileAsset.MaxFileSizeBytes} bytes.");
    }
}
