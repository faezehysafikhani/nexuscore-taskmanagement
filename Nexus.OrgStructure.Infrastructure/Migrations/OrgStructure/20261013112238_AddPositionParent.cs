using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddPositionParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentPositionId",
                schema: "org_structure",
                table: "Positions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_ParentPositionId",
                schema: "org_structure",
                table: "Positions",
                column: "ParentPositionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Positions_ParentPositionId",
                schema: "org_structure",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ParentPositionId",
                schema: "org_structure",
                table: "Positions");
        }
    }
}
