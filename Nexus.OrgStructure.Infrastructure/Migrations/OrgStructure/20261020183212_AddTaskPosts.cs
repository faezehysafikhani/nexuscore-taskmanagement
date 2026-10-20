using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddTaskPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskPosts",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    HolderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskPosts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskPosts_PostId",
                schema: "org_structure",
                table: "TaskPosts",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskPosts_TenantId_TaskId_PostId_Role",
                schema: "org_structure",
                table: "TaskPosts",
                columns: new[] { "TenantId", "TaskId", "PostId", "Role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskPosts",
                schema: "org_structure");
        }
    }
}
