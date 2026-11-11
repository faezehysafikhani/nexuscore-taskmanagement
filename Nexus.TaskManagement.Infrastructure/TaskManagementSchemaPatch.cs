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
    /// <summary>
    /// Subtasks' own people (SubTaskAssignees) and their three-state status with actual start and
    /// end dates. A subtask already completed gets status Completed and its audit time as both dates,
    /// once, when the columns are added.
    /// </summary>
    public const string AddSubTaskAssigneesAndStatusSql = """
        IF OBJECT_ID(N'[task_management].[SubTasks]') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'task_management.SubTasks', N'ActualStartedAtUtc') IS NULL
                ALTER TABLE [task_management].[SubTasks] ADD [ActualStartedAtUtc] datetimeoffset NULL;
            IF COL_LENGTH(N'task_management.SubTasks', N'ActualCompletedAtUtc') IS NULL
                ALTER TABLE [task_management].[SubTasks] ADD [ActualCompletedAtUtc] datetimeoffset NULL;
            IF COL_LENGTH(N'task_management.SubTasks', N'Status') IS NULL
            BEGIN
                ALTER TABLE [task_management].[SubTasks] ADD [Status] int NOT NULL CONSTRAINT [DF_SubTasks_Status] DEFAULT 0;
                EXEC(N'UPDATE [task_management].[SubTasks]
                    SET [Status] = 2,
                        [ActualStartedAtUtc] = COALESCE([ModifiedAtUtc], [CreatedAtUtc]),
                        [ActualCompletedAtUtc] = COALESCE([ModifiedAtUtc], [CreatedAtUtc])
                    WHERE [IsCompleted] = 1;');
            END
        END
        IF OBJECT_ID(N'[task_management].[SubTasks]') IS NOT NULL AND OBJECT_ID(N'[task_management].[SubTaskAssignees]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[SubTaskAssignees] (
                [Id] uniqueidentifier NOT NULL,
                [SubTaskId] uniqueidentifier NOT NULL,
                [UserId] uniqueidentifier NOT NULL,
                CONSTRAINT [PK_SubTaskAssignees] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_SubTaskAssignees_SubTasks_SubTaskId] FOREIGN KEY ([SubTaskId]) REFERENCES [task_management].[SubTasks] ([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_SubTaskAssignees_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [identity].[Users] ([Id]));
            CREATE UNIQUE INDEX [IX_SubTaskAssignees_SubTaskId_UserId] ON [task_management].[SubTaskAssignees] ([SubTaskId], [UserId]);
            CREATE INDEX [IX_SubTaskAssignees_UserId] ON [task_management].[SubTaskAssignees] ([UserId]);
        END
        """;

    /// <summary>Row version for edit conflicts (CAT-209) and the archive (CAT-210).</summary>
    public const string AddConcurrencyAndArchiveSql = """
        IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'task_management.Tasks', N'RowVersion') IS NULL ALTER TABLE [task_management].[Tasks] ADD [RowVersion] rowversion;
            IF COL_LENGTH(N'task_management.Tasks', N'ArchivedAtUtc') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ArchivedAtUtc] datetimeoffset NULL;
            IF COL_LENGTH(N'task_management.Tasks', N'ArchivedByUserId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ArchivedByUserId] uniqueidentifier NULL;
        END
        """;

    /// <summary>The archive's index, created after the column exists (a separate batch: SQL Server checks names per batch).</summary>
    public const string AddArchiveIndexSql = """
        IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL AND COL_LENGTH(N'task_management.Tasks', N'ArchivedAtUtc') IS NOT NULL
            AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Tasks_TenantId_ArchivedAtUtc' AND object_id = OBJECT_ID(N'[task_management].[Tasks]'))
            CREATE INDEX [IX_Tasks_TenantId_ArchivedAtUtc] ON [task_management].[Tasks] ([TenantId], [ArchivedAtUtc]);
        """;

    /// <summary>Organization-defined task statuses (CAT-252..254 design: a label on top of the four built-in statuses).</summary>
    public const string AddCustomStatusesSql = """
        IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'task_management.Tasks', N'CustomStatusId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [CustomStatusId] uniqueidentifier NULL;
        END
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskStatuses]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[TaskStatuses] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [Name] nvarchar(100) NOT NULL,
                [NormalizedName] nvarchar(100) NOT NULL,
                [Category] int NOT NULL,
                [Color] nvarchar(30) NULL,
                [SortOrder] int NOT NULL,
                [IsActive] bit NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_TaskStatuses] PRIMARY KEY ([Id]));
            CREATE UNIQUE INDEX [IX_TaskStatuses_TenantId_NormalizedName] ON [task_management].[TaskStatuses] ([TenantId], [NormalizedName]);
        END
        """;

    /// <summary>Submit-for-review state on tasks (phase 4). ReviewStatus: 0 none, 1 pending, 2 approved, 3 rejected.</summary>
    public const string AddTaskReviewSql = """
        IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'task_management.Tasks', N'ReviewStatus') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewStatus] int NOT NULL CONSTRAINT [DF_Tasks_ReviewStatus] DEFAULT 0;
            IF COL_LENGTH(N'task_management.Tasks', N'ReviewSubmittedAtUtc') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewSubmittedAtUtc] datetimeoffset NULL;
            IF COL_LENGTH(N'task_management.Tasks', N'ReviewSubmittedByUserId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewSubmittedByUserId] uniqueidentifier NULL;
            IF COL_LENGTH(N'task_management.Tasks', N'ReviewDecidedAtUtc') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewDecidedAtUtc] datetimeoffset NULL;
            IF COL_LENGTH(N'task_management.Tasks', N'ReviewDecidedByUserId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewDecidedByUserId] uniqueidentifier NULL;
            IF COL_LENGTH(N'task_management.Tasks', N'ReviewComment') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewComment] nvarchar(1000) NULL;
        END
        """;

    /// <summary>Requests with their categories, and the source a task keeps (phase 5).</summary>
    public const string AddRequestsSql = """
        IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'task_management.Tasks', N'SourceType') IS NULL ALTER TABLE [task_management].[Tasks] ADD [SourceType] nvarchar(30) NULL;
            IF COL_LENGTH(N'task_management.Tasks', N'SourceId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [SourceId] uniqueidentifier NULL;
        END
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[RequestCategories]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[RequestCategories] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [Name] nvarchar(100) NOT NULL,
                [NormalizedName] nvarchar(100) NOT NULL,
                [RouteToUserId] uniqueidentifier NULL,
                [UrgentRouteToUserId] uniqueidentifier NULL,
                [DefaultPriority] int NOT NULL,
                [IsActive] bit NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_RequestCategories] PRIMARY KEY ([Id]));
            CREATE UNIQUE INDEX [IX_RequestCategories_TenantId_NormalizedName] ON [task_management].[RequestCategories] ([TenantId], [NormalizedName]);
        END
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskRequests]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[TaskRequests] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [CategoryId] uniqueidentifier NULL,
                [Title] nvarchar(200) NOT NULL,
                [Description] nvarchar(4000) NULL,
                [Priority] int NOT NULL,
                [NeededBy] date NULL,
                [RequesterUserId] uniqueidentifier NOT NULL,
                [RoutedToUserId] uniqueidentifier NULL,
                [Status] int NOT NULL,
                [DecisionNote] nvarchar(1000) NULL,
                [DecidedByUserId] uniqueidentifier NULL,
                [DecidedAtUtc] datetimeoffset NULL,
                [ConvertedTaskId] uniqueidentifier NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_TaskRequests] PRIMARY KEY ([Id]));
            CREATE INDEX [IX_TaskRequests_TenantId_Status] ON [task_management].[TaskRequests] ([TenantId], [Status]);
            CREATE INDEX [IX_TaskRequests_TenantId_RequesterUserId] ON [task_management].[TaskRequests] ([TenantId], [RequesterUserId]);
            CREATE INDEX [IX_TaskRequests_TenantId_RoutedToUserId] ON [task_management].[TaskRequests] ([TenantId], [RoutedToUserId]);
        END
        """;

    /// <summary>Dependencies, blocker, acceptance criteria, WIP limit and templates (phase 3c).</summary>
    public const string AddPlanningSql = """
        IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL AND COL_LENGTH(N'task_management.Tasks', N'BlockedReason') IS NULL
            ALTER TABLE [task_management].[Tasks] ADD [BlockedReason] nvarchar(300) NULL;
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskDependencies]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[TaskDependencies] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [TaskId] uniqueidentifier NOT NULL,
                [BlockedByTaskId] uniqueidentifier NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_TaskDependencies] PRIMARY KEY ([Id]));
            CREATE UNIQUE INDEX [IX_TaskDependencies_TaskId_BlockedByTaskId] ON [task_management].[TaskDependencies] ([TaskId], [BlockedByTaskId]);
            CREATE INDEX [IX_TaskDependencies_BlockedByTaskId] ON [task_management].[TaskDependencies] ([BlockedByTaskId]);
        END
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskCriteria]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[TaskCriteria] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [TaskId] uniqueidentifier NOT NULL,
                [Text] nvarchar(300) NOT NULL,
                [IsMet] bit NOT NULL,
                [MetByUserId] uniqueidentifier NULL,
                [MetAtUtc] datetimeoffset NULL,
                [SortOrder] int NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_TaskCriteria] PRIMARY KEY ([Id]));
            CREATE INDEX [IX_TaskCriteria_TaskId] ON [task_management].[TaskCriteria] ([TaskId]);
        END
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskWipSettings]') IS NULL
            CREATE TABLE [task_management].[TaskWipSettings] (
                [TenantId] uniqueidentifier NOT NULL,
                [LimitPerUser] int NULL,
                CONSTRAINT [PK_TaskWipSettings] PRIMARY KEY ([TenantId]));
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskTemplates]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[TaskTemplates] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [Name] nvarchar(100) NOT NULL,
                [NormalizedName] nvarchar(100) NOT NULL,
                [IsProject] bit NOT NULL,
                [Body] nvarchar(max) NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_TaskTemplates] PRIMARY KEY ([Id]));
            CREATE UNIQUE INDEX [IX_TaskTemplates_TenantId_NormalizedName] ON [task_management].[TaskTemplates] ([TenantId], [NormalizedName]);
        END
        """;

    /// <summary>Estimates, time entries, timer and weekly timesheet submissions (phase 6).</summary>
    public const string AddTimesheetsSql = """
        IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL AND COL_LENGTH(N'task_management.Tasks', N'EstimatedMinutes') IS NULL
            ALTER TABLE [task_management].[Tasks] ADD [EstimatedMinutes] int NULL;
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TimeEntries]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[TimeEntries] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [TaskId] uniqueidentifier NOT NULL,
                [UserId] uniqueidentifier NOT NULL,
                [Date] date NOT NULL,
                [Minutes] int NOT NULL,
                [Note] nvarchar(500) NULL,
                [Source] int NOT NULL,
                [Status] int NOT NULL,
                [SubmissionId] uniqueidentifier NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_TimeEntries] PRIMARY KEY ([Id]));
            CREATE INDEX [IX_TimeEntries_TenantId_UserId_Date] ON [task_management].[TimeEntries] ([TenantId], [UserId], [Date]);
            CREATE INDEX [IX_TimeEntries_TaskId] ON [task_management].[TimeEntries] ([TaskId]);
            CREATE INDEX [IX_TimeEntries_SubmissionId] ON [task_management].[TimeEntries] ([SubmissionId]);
        END
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TimesheetSubmissions]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[TimesheetSubmissions] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [UserId] uniqueidentifier NOT NULL,
                [WeekStart] date NOT NULL,
                [TotalMinutes] int NOT NULL,
                [SubmittedAtUtc] datetimeoffset NOT NULL,
                [Status] int NOT NULL,
                [DecidedByUserId] uniqueidentifier NULL,
                [DecidedAtUtc] datetimeoffset NULL,
                [DecisionComment] nvarchar(1000) NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_TimesheetSubmissions] PRIMARY KEY ([Id]));
            CREATE INDEX [IX_TimesheetSubmissions_TenantId_UserId_WeekStart] ON [task_management].[TimesheetSubmissions] ([TenantId], [UserId], [WeekStart]);
        END
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[RunningTimers]') IS NULL
            CREATE TABLE [task_management].[RunningTimers] (
                [UserId] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [TaskId] uniqueidentifier NOT NULL,
                [StartedAtUtc] datetimeoffset NOT NULL,
                CONSTRAINT [PK_RunningTimers] PRIMARY KEY ([UserId]));
        """;

    /// <summary>The index on the new column, in its own batch.</summary>
    public const string AddCustomStatusIndexSql = """
        IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL AND COL_LENGTH(N'task_management.Tasks', N'CustomStatusId') IS NOT NULL
            AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Tasks_TenantId_CustomStatusId' AND object_id = OBJECT_ID(N'[task_management].[Tasks]'))
            CREATE INDEX [IX_Tasks_TenantId_CustomStatusId] ON [task_management].[Tasks] ([TenantId], [CustomStatusId]);
        """;

    /// <summary>The notification outbox (RC-07 / OPEN-04).</summary>
    public const string AddNotificationOutboxSql = """
        IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[NotificationOutbox]') IS NULL
        BEGIN
            CREATE TABLE [task_management].[NotificationOutbox] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [TaskId] uniqueidentifier NOT NULL,
                [Title] nvarchar(200) NOT NULL,
                [Description] nvarchar(4000) NULL,
                [DueDate] date NOT NULL,
                [DueAtText] nvarchar(100) NULL,
                [RecipientUserId] uniqueidentifier NOT NULL,
                [DedupeKey] nvarchar(120) NOT NULL,
                [Attempts] int NOT NULL,
                [NextAttemptAtUtc] datetimeoffset NOT NULL,
                [DeliveredAtUtc] datetimeoffset NULL,
                [LastError] nvarchar(500) NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                CONSTRAINT [PK_NotificationOutbox] PRIMARY KEY ([Id]));
            CREATE UNIQUE INDEX [IX_NotificationOutbox_DedupeKey] ON [task_management].[NotificationOutbox] ([DedupeKey]);
            CREATE INDEX [IX_NotificationOutbox_DeliveredAtUtc_NextAttemptAtUtc] ON [task_management].[NotificationOutbox] ([DeliveredAtUtc], [NextAttemptAtUtc]);
        END
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
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddSubTaskAssigneesAndStatusSql, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddNotificationOutboxSql, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddConcurrencyAndArchiveSql, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddArchiveIndexSql, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddCustomStatusesSql, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddCustomStatusIndexSql, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddTaskReviewSql, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddRequestsSql, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddPlanningSql, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(TaskManagementSchema.AddTimesheetsSql, cancellationToken);
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
