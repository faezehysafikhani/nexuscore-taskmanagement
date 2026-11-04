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
            // The same idempotent statements the module runs on start-up (TaskManagementSchemaPatcher),
            // so the migration also succeeds on a database that already has them, and a database kept
            // either way ends up the same.
            migrationBuilder.Sql(TaskManagementSchema.AddSubTaskAssigneesAndStatusSql);
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
