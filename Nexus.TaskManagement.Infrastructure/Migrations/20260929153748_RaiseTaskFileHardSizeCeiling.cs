using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RaiseTaskFileHardSizeCeiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only the Files size ceiling changes here. (IsResponsible on TaskAssignees was
            // already added by 20260926133806_AddIsResponsible; this migration does not touch it
            // - it only brings the checked-in model snapshot back in sync with that migration.)
            migrationBuilder.DropCheckConstraint(
                name: "CK_Files_MaxSize",
                schema: "task_management",
                table: "Files");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Files_MaxSize",
                schema: "task_management",
                table: "Files",
                sql: "[FileSizeBytes] > 0 AND [FileSizeBytes] <= 20971520");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Files_MaxSize",
                schema: "task_management",
                table: "Files");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Files_MaxSize",
                schema: "task_management",
                table: "Files",
                sql: "[FileSizeBytes] > 0 AND [FileSizeBytes] <= 204800");
        }
    }
}
