using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddTeamsAndTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                schema: "org_structure",
                table: "UserAssignments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Honorifics",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Honorifics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LeaderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserHonorifics",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HonorificId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserHonorifics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAssignments_TeamId",
                schema: "org_structure",
                table: "UserAssignments",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Honorifics_TenantId_Title",
                schema: "org_structure",
                table: "Honorifics",
                columns: new[] { "TenantId", "Title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_TenantId_UnitId_Name",
                schema: "org_structure",
                table: "Teams",
                columns: new[] { "TenantId", "UnitId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserHonorifics_HonorificId",
                schema: "org_structure",
                table: "UserHonorifics",
                column: "HonorificId");

            migrationBuilder.CreateIndex(
                name: "IX_UserHonorifics_TenantId_UserId",
                schema: "org_structure",
                table: "UserHonorifics",
                columns: new[] { "TenantId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Honorifics",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "Teams",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "UserHonorifics",
                schema: "org_structure");

            migrationBuilder.DropIndex(
                name: "IX_UserAssignments_TeamId",
                schema: "org_structure",
                table: "UserAssignments");

            migrationBuilder.DropColumn(
                name: "TeamId",
                schema: "org_structure",
                table: "UserAssignments");
        }
    }
}
