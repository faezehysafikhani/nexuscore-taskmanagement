-- Upgrade 20: work calendars (CAT-85) - working days, hours per day, official holidays and date exceptions.
-- Idempotent: safe to run again. The API creates the same tables at start-up when
-- Database:SeedOnStartup is on; run this when the database is managed by scripts instead.
IF SCHEMA_ID(N'calendar') IS NULL EXEC(N'CREATE SCHEMA [calendar];');
GO

IF OBJECT_ID(N'[calendar].[WorkCalendars]') IS NULL
BEGIN
    CREATE TABLE [calendar].[WorkCalendars] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Description] nvarchar(500) NULL,
        [WorkingDays] int NOT NULL,
        [IsDefault] bit NOT NULL,
        [WorkHoursPerDay] int NOT NULL CONSTRAINT [DF_WorkCalendars_WorkHoursPerDay] DEFAULT 8,
        [ApplyOfficialHolidays] bit NOT NULL CONSTRAINT [DF_WorkCalendars_ApplyOfficialHolidays] DEFAULT 0,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_WorkCalendars] PRIMARY KEY ([Id])
    );
END
GO

IF OBJECT_ID(N'[calendar].[WorkCalendarExceptions]') IS NULL
BEGIN
    CREATE TABLE [calendar].[WorkCalendarExceptions] (
        [Id] uniqueidentifier NOT NULL,
        [CalendarId] uniqueidentifier NOT NULL,
        [Date] date NOT NULL,
        [IsWorkingDay] bit NOT NULL,
        [Description] nvarchar(300) NULL,
        CONSTRAINT [PK_WorkCalendarExceptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkCalendarExceptions_WorkCalendars_CalendarId] FOREIGN KEY ([CalendarId]) REFERENCES [calendar].[WorkCalendars] ([Id]) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX [IX_WorkCalendarExceptions_CalendarId_Date] ON [calendar].[WorkCalendarExceptions] ([CalendarId], [Date]);
END
GO
