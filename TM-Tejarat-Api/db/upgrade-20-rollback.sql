-- Rollback of upgrade 20 (work calendars): removes the tables and every calendar defined in them.
IF OBJECT_ID(N'[calendar].[WorkCalendarExceptions]') IS NOT NULL DROP TABLE [calendar].[WorkCalendarExceptions];
IF OBJECT_ID(N'[calendar].[WorkCalendars]') IS NOT NULL DROP TABLE [calendar].[WorkCalendars];
GO
