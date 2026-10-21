using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubTaskStatusAndActualDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActualCompletedAtUtc",
                schema: "task_management",
                table: "SubTasks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActualStartedAtUtc",
                schema: "task_management",
                table: "SubTasks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "task_management",
                table: "SubTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill: a subtask already marked completed before this migration existed has no
            // recorded actual dates, so approximate them from its audit timestamps.
            migrationBuilder.Sql(
                """
                UPDATE [task_management].[SubTasks]
                SET [Status] = 2,
                    [ActualStartedAtUtc] = COALESCE([ModifiedAtUtc], [CreatedAtUtc]),
                    [ActualCompletedAtUtc] = COALESCE([ModifiedAtUtc], [CreatedAtUtc])
                WHERE [IsCompleted] = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualCompletedAtUtc",
                schema: "task_management",
                table: "SubTasks");

            migrationBuilder.DropColumn(
                name: "ActualStartedAtUtc",
                schema: "task_management",
                table: "SubTasks");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "task_management",
                table: "SubTasks");
        }
    }
}
