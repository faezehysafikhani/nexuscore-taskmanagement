using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddPostParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentPostId",
                schema: "org_structure",
                table: "UnitPositions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitPositions_ParentPostId",
                schema: "org_structure",
                table: "UnitPositions",
                column: "ParentPostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UnitPositions_ParentPostId",
                schema: "org_structure",
                table: "UnitPositions");

            migrationBuilder.DropColumn(
                name: "ParentPostId",
                schema: "org_structure",
                table: "UnitPositions");
        }
    }
}
