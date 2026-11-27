using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.TaskManagement.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.TaskManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTaskManagementInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<TaskManagementDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<ITaskManagementUnitOfWork>(provider =>
            provider.GetRequiredService<TaskManagementDbContext>());

        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IRepetitiveTaskRepository, RepetitiveTaskRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ITaskStatusRepository, TaskStatusRepository>();
        services.AddScoped<ITaskExtrasRepository, TaskExtrasRepository>();
        services.AddScoped<ITaskRequestRepository, TaskRequestRepository>();
        services.AddScoped<ITaskPlanningRepository, TaskPlanningRepository>();
        services.AddScoped<ITimesheetRepository, TimesheetRepository>();
        services.AddScoped<IAutomationRepository, AutomationRepository>();
        services.AddScoped<IWebhookRepository, WebhookRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IRetentionRepository, RetentionRepository>();
        services.AddScoped<IReportSubscriptionRepository, ReportSubscriptionRepository>();
        services.AddScoped<ITaskDigestSource, TaskDigestSource>();
        // First of all hosted services (the host starts them one by one, in this order): the columns
        // and tables newer versions need, added to an existing database before any job reads them.
        services.AddHostedService<TaskManagementSchemaPatcher>();
        // Background jobs of commercial features run only where the host turns the feature on.
        bool On(string feature) => NexusCore.Application.Editions.EditionFeatures.IsEnabled(configuration, feature);
        if (On(NexusCore.Application.Editions.CommercialFeature.Automation) || On(NexusCore.Application.Editions.CommercialFeature.Sla))
        {
            services.AddHostedService<TaskSlaMonitorService>();
        }

        services.Configure<Nexus.TaskManagement.Application.WebhookOptions>(configuration.GetSection(Nexus.TaskManagement.Application.WebhookOptions.SectionName));
        services.AddSingleton<WebhookDeliveryService>();
        if (On(NexusCore.Application.Editions.CommercialFeature.Webhooks))
        {
            services.AddHostedService<WebhookDeliveryBackgroundService>();
        }

        services.AddSingleton<ReportDistributionService>();
        if (On(NexusCore.Application.Editions.CommercialFeature.ReportSubscriptions))
        {
            services.AddHostedService(p => p.GetRequiredService<ReportDistributionService>());
        }

        services.AddSingleton<TaskRetentionBackgroundService>();
        if (On(NexusCore.Application.Editions.CommercialFeature.Retention))
        {
            services.AddHostedService(p => p.GetRequiredService<TaskRetentionBackgroundService>());
        }
        services.AddScoped<ITaskFileRepository, TaskFileRepository>();
        services.AddScoped<INoteRepository, NoteRepository>();
        services.AddScoped<ITaskCommentRepository, TaskCommentRepository>();

        // Task history is served from NexusCore's AuditLog, so this one lives in
        // Infrastructure where NexusCoreDbContext is reachable.
        services.AddScoped<ITaskActivityService, TaskActivityService>();

        services.Configure<TaskSmsOptions>(configuration.GetSection(TaskSmsOptions.SectionName));
        services.Configure<RecurrenceOptions>(configuration.GetSection(RecurrenceOptions.SectionName));
        services.Configure<RepetitiveTaskSchedulerOptions>(
            configuration.GetSection(RepetitiveTaskSchedulerOptions.SectionName));

        services.AddScoped<ITaskSmsOutbox, TaskSmsOutbox>();
        // Notifications are written down first and delivered from there: a failed one is retried, never repeated.
        services.AddScoped<ITaskNotificationOutbox, TaskNotificationOutbox>();
        services.AddHostedService<TaskNotificationOutboxService>();
        services.TryAddSingleton(TimeProvider.System);

        // Registered unconditionally; it checks its own Enabled flag and exits immediately
        // when off, which keeps the "is the job running" answer in one place.
        services.AddHostedService<RepetitiveTaskSchedulerService>();
        // Due-soon, overdue and coming-up reminders, with the same switch and interval.
        services.AddSingleton<TaskReminderService>();
        services.AddHostedService(provider => provider.GetRequiredService<TaskReminderService>());

        return services;
    }
}
