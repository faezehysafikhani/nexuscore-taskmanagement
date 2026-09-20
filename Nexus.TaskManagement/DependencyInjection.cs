using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IRepetitiveTaskService, RepetitiveTaskService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<ITaskFileService, TaskFileService>();
        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<ITaskCommentService, TaskCommentService>();

        services.AddSingleton<IRecurrenceCalculator, RecurrenceCalculator>();

        // Reacts to a recurring task falling due. Dispatched by DomainEventDispatchInterceptor.
        services.AddScoped<IDomainEventHandler<RepetitiveTaskDue>, RepetitiveTaskDueHandler>();

        // Defaults for the two outbound seams. TryAdd, so a host that installs the
        // notification integration - or a real SMS gateway - wins without touching this file.
        services.TryAddScoped<ITaskNotificationPublisher, NullTaskNotificationPublisher>();
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
        services.AddScoped<IValidator<CreateRepetitiveTaskRequest>, CreateRepetitiveTaskRequestValidator>();
        services.AddScoped<IValidator<UpdateRepetitiveTaskRequest>, UpdateRepetitiveTaskRequestValidator>();
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
