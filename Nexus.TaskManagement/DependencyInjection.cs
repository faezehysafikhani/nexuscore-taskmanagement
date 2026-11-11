using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexusCore.Application.Common;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Application.Validators;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the TaskManagement application tier. Pair it with
    /// AddTaskManagementInfrastructure and MapTaskManagementEndpoints; dropping all three
    /// removes the module with nothing left behind.
    /// </summary>
    public static IServiceCollection AddTaskManagement(this IServiceCollection services)
    {
        services.TryAddSingleton<ISchedulingDayPolicy, AllowAllSchedulingDays>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IRepetitiveTaskService, RepetitiveTaskService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<ITaskStatusService, TaskStatusService>();
        services.AddScoped<ITaskBatchService, TaskBatchService>();
        services.AddScoped<ITaskReviewService, TaskReviewService>();
        services.AddScoped<ITaskRequestService, TaskRequestService>();
        services.AddScoped<ITaskPlanningService, TaskPlanningService>();
        services.AddScoped<ITimesheetService, TimesheetService>();
        services.AddScoped<ITaskAutomation, TaskAutomationEngine>();
        services.AddScoped<IAutomationService, AutomationService>();
        services.AddScoped<NexusCore.SharedKernel.Domain.IDomainEventHandler<NexusCore.Application.Approvals.ApprovalGranted>, TimesheetApprovedHandler>();
        services.AddScoped<NexusCore.SharedKernel.Domain.IDomainEventHandler<NexusCore.Application.Approvals.ApprovalRejected>, TimesheetRejectedHandler>();
        services.AddScoped<ITaskTemplateService, TaskTemplateService>();
        services.AddScoped<NexusCore.SharedKernel.Domain.IDomainEventHandler<NexusCore.Application.Approvals.ApprovalGranted>, TaskReviewApprovedHandler>();
        services.AddScoped<NexusCore.SharedKernel.Domain.IDomainEventHandler<NexusCore.Application.Approvals.ApprovalRejected>, TaskReviewRejectedHandler>();
        services.AddScoped<ITaskFileService, TaskFileService>();
        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<ITaskCommentService, TaskCommentService>();
        services.AddScoped<ITaskAccessScope, TaskAccessScope>();
        // Whom a user may make responsible: anyone, unless a product limits it (Nexus.OrgStructure).
        services.TryAddScoped<NexusCore.Application.Identity.Interfaces.IWorkAssignmentScope,
            NexusCore.Application.Identity.Interfaces.UnlimitedWorkAssignmentScope>();
        // Everyone in the tenant may be reached, unless a product limits it (Nexus.OrgStructure).
        services.TryAddScoped<NexusCore.Application.Identity.Interfaces.IUserReachScope,
            NexusCore.Application.Identity.Interfaces.UnlimitedUserReachScope>();
        // Tasks are given to people, unless a product gives them to posts (Nexus.OrgStructure).
        services.TryAddScoped<NexusCore.Application.Identity.Interfaces.ITaskPosts,
            NexusCore.Application.Identity.Interfaces.NoTaskPosts>();
        services.AddScoped<NexusCore.Application.Identity.Interfaces.ITaskPeopleUpdater, TaskPeopleUpdater>();

        // In the users' time zone (TaskManagement:Recurrence:TimeZone, Asia/Tehran by default).
        services.AddSingleton<IRecurrenceCalculator>(provider => new RecurrenceCalculator(RecurrenceCalculator.ResolveTimeZone(
            (provider.GetService<Microsoft.Extensions.Options.IOptions<RecurrenceOptions>>()?.Value ?? new RecurrenceOptions()).TimeZone)));

        // Reacts to a recurring task falling due. Dispatched by DomainEventDispatchInterceptor.
        services.AddScoped<IDomainEventHandler<RepetitiveTaskDue>, RepetitiveTaskDueHandler>();

        // Defaults for the two outbound seams. TryAdd, so a host that installs the
        // notification integration - or a real SMS gateway - wins without touching this file.
        services.TryAddScoped<ITaskNotificationPublisher, NullTaskNotificationPublisher>();
        services.TryAddScoped<ITaskNoticePublisher, NullTaskNoticePublisher>();
        services.AddScoped<ITaskReminderSettingsService, TaskReminderSettingsService>();
        services.AddScoped<TaskReminderDispatcher>();
        services.TryAddScoped<IUserContactResolver, UnavailableUserContactResolver>();
        services.TryAddScoped<ITaskSmsSender, LoggingTaskSmsSender>();

        // Registered one by one, the way the sibling modules do it - the assembly-scanning
        // helper lives in a package this solution does not reference.
        services.AddScoped<IValidator<CreateTaskRequest>, CreateTaskRequestValidator>();
        services.AddScoped<IValidator<UpdateTaskRequest>, UpdateTaskRequestValidator>();
        services.AddScoped<IValidator<ChangeTaskStatusRequest>, ChangeTaskStatusRequestValidator>();
        services.AddScoped<IValidator<ChangeTaskPriorityRequest>, ChangeTaskPriorityRequestValidator>();
        services.AddScoped<IValidator<AssignUserRequest>, AssignUserRequestValidator>();
        services.AddScoped<IValidator<AssignUserGroupRequest>, AssignUserGroupRequestValidator>();
        services.AddScoped<IValidator<CreateSubTaskRequest>, CreateSubTaskRequestValidator>();
        services.AddScoped<IValidator<UpdateSubTaskRequest>, UpdateSubTaskRequestValidator>();
        services.AddScoped<IValidator<ChangeSubTaskStatusRequest>, ChangeSubTaskStatusRequestValidator>();
        services.AddScoped<IValidator<CreateRepetitiveTaskRequest>, CreateRepetitiveTaskRequestValidator>();
        services.AddScoped<IValidator<UpdateRepetitiveTaskRequest>, UpdateRepetitiveTaskRequestValidator>();
        services.AddScoped<IValidator<SubmitTaskReviewRequest>, SubmitTaskReviewRequestValidator>();
        services.AddScoped<IValidator<SaveAutomationRuleRequest>, SaveAutomationRuleRequestValidator>();
        services.AddScoped<IValidator<LogTimeRequest>, LogTimeRequestValidator>();
        services.AddScoped<IValidator<UpdateTimeEntryRequest>, UpdateTimeEntryRequestValidator>();
        services.AddScoped<IValidator<SaveCriterionRequest>, SaveCriterionRequestValidator>();
        services.AddScoped<IValidator<SetTaskBlockerRequest>, SetTaskBlockerRequestValidator>();
        services.AddScoped<IValidator<BulkSubTasksRequest>, BulkSubTasksRequestValidator>();
        services.AddScoped<IValidator<SaveTaskTemplateRequest>, SaveTaskTemplateRequestValidator>();
        services.AddScoped<IValidator<SaveTaskAsTemplateRequest>, SaveTaskAsTemplateRequestValidator>();
        services.AddScoped<IValidator<SaveRequestCategoryRequest>, SaveRequestCategoryRequestValidator>();
        services.AddScoped<IValidator<SubmitTaskRequestRequest>, SubmitTaskRequestRequestValidator>();
        services.AddScoped<IValidator<RejectTaskRequestRequest>, RejectTaskRequestRequestValidator>();
        services.AddScoped<IValidator<TaskFromMessageRequest>, TaskFromMessageRequestValidator>();
        services.AddScoped<IValidator<QuickCreateTaskRequest>, QuickCreateTaskRequestValidator>();
        services.AddScoped<IValidator<BulkTaskRequest>, BulkTaskRequestValidator>();
        services.AddScoped<IValidator<CreateTaskStatusRequest>, CreateTaskStatusRequestValidator>();
        services.AddScoped<IValidator<UpdateTaskStatusRequest>, UpdateTaskStatusRequestValidator>();
        services.AddScoped<IValidator<CreateTagRequest>, CreateTagRequestValidator>();
        services.AddScoped<IValidator<UpdateTagRequest>, UpdateTagRequestValidator>();
        services.AddScoped<IValidator<AssignTagRequest>, AssignTagRequestValidator>();
        services.AddScoped<IValidator<CreateNoteRequest>, CreateNoteRequestValidator>();
        services.AddScoped<IValidator<UpdateNoteRequest>, UpdateNoteRequestValidator>();
        services.AddScoped<IValidator<CreateTaskCommentRequest>, CreateTaskCommentRequestValidator>();
        services.AddScoped<IValidator<UpdateTaskCommentRequest>, UpdateTaskCommentRequestValidator>();
        services.AddScoped<IValidator<UploadFileRequest>, UploadFileRequestValidator>();

        services.AddSingleton<IPermissionCatalog, TaskManagementPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in TaskManagementPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
