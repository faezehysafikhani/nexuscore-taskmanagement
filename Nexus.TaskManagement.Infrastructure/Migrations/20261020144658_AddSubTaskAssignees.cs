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
            migrationBuilder.CreateTable(
                name: "SubTaskAssignees",
                schema: "task_management",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubTaskAssignees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubTaskAssignees_SubTasks_SubTaskId",
                        column: x => x.SubTaskId,
                        principalSchema: "task_management",
                        principalTable: "SubTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubTaskAssignees_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskAssignees_SubTaskId_UserId",
                schema: "task_management",
                table: "SubTaskAssignees",
                columns: new[] { "SubTaskId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubTaskAssignees_UserId",
                schema: "task_management",
                table: "SubTaskAssignees",
                column: "UserId");
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
