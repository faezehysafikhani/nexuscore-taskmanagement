using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddPositionPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PositionPermissions",
                schema: "org_structure",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionPermissions", x => new { x.CompanyId, x.PositionId, x.PermissionName });
                });

            migrationBuilder.CreateTable(
                name: "UnitPositionPermissions",
                schema: "org_structure",
                columns: table => new
                {
                    UnitPositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitPositionPermissions", x => new { x.UnitPositionId, x.PermissionName });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PositionPermissions_TenantId",
                schema: "org_structure",
                table: "PositionPermissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitPositionPermissions_TenantId",
                schema: "org_structure",
                table: "UnitPositionPermissions",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PositionPermissions",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "UnitPositionPermissions",
                schema: "org_structure");
        }
    }
}
