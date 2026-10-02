using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        // Registered unconditionally; it checks its own Enabled flag and exits immediately
        // when off, which keeps the "is the job running" answer in one place.
        services.AddHostedService<RepetitiveTaskSchedulerService>();

        return services;
    }
}
