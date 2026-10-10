using Microsoft.EntityFrameworkCore;

namespace Nexus.Calendar.Infrastructure;

/// <summary>
/// Adds the working-hours and official-holidays settings to an existing deployment's work calendars. The
/// module's tables are created by ModuleSchemaInitializer.EnsureCreatedAsync, which never adds a column to a
/// table that already exists. A host that runs EnsureCreatedAsync for this module can call this right after it on
/// every start; it is a no-op once the columns exist. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-10-add-calendar-policy.sql, which does the same thing.
/// </summary>
public static class CalendarSchemaUpgrade
{
    // Run one at a time (each its own batch, as the GO-separated script is), in this order, in one transaction.
    internal static readonly string[] Statements =
    [
        """
        IF COL_LENGTH(N'calendar.WorkCalendars', N'WorkHoursPerDay') IS NULL
            ALTER TABLE [calendar].[WorkCalendars] ADD [WorkHoursPerDay] int NOT NULL CONSTRAINT [DF_WorkCalendars_WorkHoursPerDay] DEFAULT 8;
        """,

        // 0: a calendar that already exists keeps scheduling exactly as before; only calendars created afterwards follow official holidays by default.
        """
        IF COL_LENGTH(N'calendar.WorkCalendars', N'ApplyOfficialHolidays') IS NULL
            ALTER TABLE [calendar].[WorkCalendars] ADD [ApplyOfficialHolidays] bit NOT NULL CONSTRAINT [DF_WorkCalendars_ApplyOfficialHolidays] DEFAULT 0;
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext calendarDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await calendarDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await calendarDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
