/*
    Create the meetings schema on an EXISTING NexusCore database (the DefaultConnection database)
    for the Nexus.ProjectManagement.Meetings module: project meetings with their invited people,
    attendance and minutes.

    What it does, in order:
      1. creates the [meetings] schema;
      2. creates [meetings].[Meetings] and [meetings].[MeetingParticipants];
      3. creates the two indexes.

    Nothing existing is changed or dropped. The module starts empty.

    Why a script: a host that already runs ModuleSchemaInitializer.EnsureCreatedAsync for MeetingDbContext
    (Rozet.Api does) gets all of this on its own the first time it starts, because the schema is new.
    Hosts that manage the schema by script instead run this file. Safe to run more than once, and safe to
    run after EnsureCreatedAsync has already created the tables.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-19-add-meetings.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

IF SCHEMA_ID(N'meetings') IS NULL EXEC(N'CREATE SCHEMA [meetings];');
GO

IF OBJECT_ID(N'[meetings].[Meetings]', N'U') IS NULL
    CREATE TABLE [meetings].[Meetings] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Agenda] nvarchar(4000) NULL,
        [MeetingDate] date NOT NULL,
        [StartTime] time NULL,
        [DurationMinutes] int NULL,
        [Location] nvarchar(300) NULL,
        [OnlineLink] nvarchar(500) NULL,
        [OrganizerUserId] uniqueidentifier NULL,
        [Status] int NOT NULL,
        [Minutes] nvarchar(max) NULL,
        [MinutesRecordedByUserId] uniqueidentifier NULL,
        [MinutesRecordedAtUtc] datetimeoffset NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Meetings] PRIMARY KEY ([Id])
    );
GO

IF OBJECT_ID(N'[meetings].[MeetingParticipants]', N'U') IS NULL
    CREATE TABLE [meetings].[MeetingParticipants] (
        [Id] uniqueidentifier NOT NULL,
        [MeetingId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [IsRequired] bit NOT NULL,
        [Attendance] int NOT NULL,
        CONSTRAINT [PK_MeetingParticipants] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MeetingParticipants_Meetings_MeetingId] FOREIGN KEY ([MeetingId]) REFERENCES [meetings].[Meetings] ([Id]) ON DELETE CASCADE
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MeetingParticipants_MeetingId_UserId' AND object_id = OBJECT_ID(N'[meetings].[MeetingParticipants]'))
    CREATE UNIQUE INDEX [IX_MeetingParticipants_MeetingId_UserId] ON [meetings].[MeetingParticipants] ([MeetingId], [UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Meetings_TenantId_ProjectId_MeetingDate' AND object_id = OBJECT_ID(N'[meetings].[Meetings]'))
    CREATE INDEX [IX_Meetings_TenantId_ProjectId_MeetingDate] ON [meetings].[Meetings] ([TenantId], [ProjectId], [MeetingDate]);
GO

COMMIT TRANSACTION;
GO
