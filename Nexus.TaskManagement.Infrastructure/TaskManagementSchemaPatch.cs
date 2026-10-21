using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nexus.TaskManagement.Infrastructure;

/// <summary>
/// Columns and tables added to the module after its tables were first created. Hosts create the
/// tables with EnsureCreated, which never changes an existing table, so the module adds its own on
/// start-up - idempotent, a no-op once they are there. The migration that adds them runs the same
/// statements, so a database kept by migrations and one kept this way agree.
/// </summary>
public static class TaskManagementSchema
{
    public const string AddRemindersSql = """
        IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'task_management.Tasks', N'DueSoonNotifiedFor') IS NULL ALTER TABLE [task_management].[Tasks] ADD [DueSoonNotifiedFor] datetimeoffset NULL;
            IF COL_LENGTH(N'task_management.Tasks', N'OverdueNotifiedFor') IS NULL ALTER TABLE [task_management].[Tasks] ADD [OverdueNotifiedFor] datetimeoffset NULL;
            IF COL_LENGTH(N'task_management.Tasks', N'ParentTaskId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ParentTaskId] uniqueidentifier NULL;
        END
        IF OBJECT_ID(N'[task_management].[RepetitiveTasks]') IS NOT NULL
            AND COL_LENGTH(N'task_management.RepetitiveTasks', N'UpcomingNotifiedFor') IS NULL
            ALTER TABLE [task_management].[RepetitiveTasks] ADD [UpcomingNotifiedFor] datetimeoffset NULL;
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[PendingSms]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[PendingSms] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [PhoneNumber] nvarchar(32) NOT NULL,
                [Text] nvarchar(1000) NOT NULL,
                [SendAfterUtc] datetimeoffset NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                CONSTRAINT [PK_PendingSms] PRIMARY KEY ([Id]));
            CREATE INDEX [IX_PendingSms_SendAfterUtc] ON [task_management].[PendingSms] ([SendAfterUtc]);
        END
        """;

    /// <summary>Tasks owned through a post, stand-ins for vacant posts (جانشین) and their comments.</summary>
    public const string AddPostOwnershipAndActingSql = """
        IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL AND COL_LENGTH(N'task_management.Tasks', N'OwnerHeldByPost') IS NULL
            ALTER TABLE [task_management].[Tasks] ADD [OwnerHeldByPost] bit NOT NULL CONSTRAINT [DF_Tasks_OwnerHeldByPost] DEFAULT CAST(0 AS bit);
        IF OBJECT_ID(N'[task_management].[TaskAssignees]') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'task_management.TaskAssignees', N'IsActing') IS NULL
                ALTER TABLE [task_management].[TaskAssignees] ADD [IsActing] bit NOT NULL CONSTRAINT [DF_TaskAssignees_IsActing] DEFAULT CAST(0 AS bit);
            IF COL_LENGTH(N'task_management.TaskAssignees', N'ActingNote') IS NULL
                ALTER TABLE [task_management].[TaskAssignees] ADD [ActingNote] nvarchar(200) NULL;
        END
        IF OBJECT_ID(N'[task_management].[TaskComments]') IS NOT NULL AND COL_LENGTH(N'task_management.TaskComments', N'ActingNote') IS NULL
            ALTER TABLE [task_management].[TaskComments] ADD [ActingNote] nvarchar(200) NULL;
        """;
}

/// <summary>Applies <see cref="TaskManagementSchema"/> when the host starts (SQL Server only), before the module's jobs run.</summary>
public sealed class TaskManagementSchemaPatcher(IServiceScopeFactory scopes, ILogger<TaskManagementSchemaPatcher> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            if (db.Database.IsSqlServer())
            {
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddRemindersSql, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddPostOwnershipAndActingSql, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not add the task reminder columns; apply the TaskManagement migrations.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Stores held-back SMS in a context of its own, so nothing else pending is saved with it.</summary>
public sealed class TaskSmsOutbox(IServiceScopeFactory scopes) : Application.ITaskSmsOutbox
{
    public async Task AddAsync(Domain.PendingTaskSms sms, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        db.PendingSms.Add(sms);
        await db.SaveChangesAsync(cancellationToken);
    }
}
