using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubTaskAssignees : Migration
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
            migrationBuilder.DropTable(
                name: "SubTaskAssignees",
                schema: "task_management");
        }
    }
}
