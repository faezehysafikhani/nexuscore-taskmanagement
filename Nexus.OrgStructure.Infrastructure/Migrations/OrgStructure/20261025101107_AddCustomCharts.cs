using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddCustomCharts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomCharts",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Selection = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShowHolders = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomCharts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomCharts_TenantId",
                schema: "org_structure",
                table: "CustomCharts",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomCharts",
                schema: "org_structure");
        }
    }
}
