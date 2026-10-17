using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRemindersAndPendingSms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The same idempotent statements the module runs on start-up (TaskManagementSchemaPatcher),
            // so the migration also succeeds on a database that already has them.
            migrationBuilder.Sql(TaskManagementSchema.AddRemindersSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingSms",
                schema: "task_management");

            migrationBuilder.DropColumn(
                name: "DueSoonNotifiedFor",
                schema: "task_management",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "OverdueNotifiedFor",
                schema: "task_management",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "UpcomingNotifiedFor",
                schema: "task_management",
                table: "RepetitiveTasks");
        }
    }
}
