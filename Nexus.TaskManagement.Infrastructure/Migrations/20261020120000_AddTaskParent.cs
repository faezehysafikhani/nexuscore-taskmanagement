using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The same idempotent statements the module runs on start-up (TaskManagementSchemaPatcher),
            // so the migration also succeeds on a database that already has the column.
            migrationBuilder.Sql(TaskManagementSchema.AddRemindersSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParentTaskId",
                schema: "task_management",
                table: "Tasks");
        }
    }
}
