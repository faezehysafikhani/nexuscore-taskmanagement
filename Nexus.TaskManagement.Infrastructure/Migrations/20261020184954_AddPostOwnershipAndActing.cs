using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostOwnershipAndActing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The same idempotent statements the module runs on start-up (TaskManagementSchemaPatcher),
            // so the migration also succeeds on a database that already has them.
            migrationBuilder.Sql(TaskManagementSchema.AddPostOwnershipAndActingSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerHeldByPost",
                schema: "task_management",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ActingNote",
                schema: "task_management",
                table: "TaskComments");

            migrationBuilder.DropColumn(
                name: "ActingNote",
                schema: "task_management",
                table: "TaskAssignees");

            migrationBuilder.DropColumn(
                name: "IsActing",
                schema: "task_management",
                table: "TaskAssignees");
        }
    }
}
