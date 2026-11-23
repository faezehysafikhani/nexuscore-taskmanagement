SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF SCHEMA_ID(N'calendar') IS NULL EXEC(N'CREATE SCHEMA [calendar]');
IF OBJECT_ID(N'calendar.Meetings', N'U') IS NULL
BEGIN
    CREATE TABLE [calendar].[Meetings] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [OrganizerUserId] uniqueidentifier NOT NULL,
        [Date] date NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [Version] int NOT NULL,
        CONSTRAINT [PK_Meetings] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_Meetings_TenantId_Date] ON [calendar].[Meetings] ([TenantId],[Date]);
END;
IF OBJECT_ID(N'calendar.MeetingParticipants', N'U') IS NULL
BEGIN
    CREATE TABLE [calendar].[MeetingParticipants] (
        [MeetingId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MeetingParticipants] PRIMARY KEY ([MeetingId],[UserId]),
        CONSTRAINT [FK_MeetingParticipants_Meetings_MeetingId] FOREIGN KEY ([MeetingId]) REFERENCES [calendar].[Meetings]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_MeetingParticipants_UserId] ON [calendar].[MeetingParticipants] ([UserId]);
END;
COMMIT;
