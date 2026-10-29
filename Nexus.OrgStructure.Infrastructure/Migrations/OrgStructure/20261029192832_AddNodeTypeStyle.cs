using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddNodeTypeStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                schema: "org_structure",
                table: "NodeTypes",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#2563eb");

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                schema: "org_structure",
                table: "NodeTypes",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "folder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                schema: "org_structure",
                table: "NodeTypes");

            migrationBuilder.DropColumn(
                name: "Icon",
                schema: "org_structure",
                table: "NodeTypes");
        }
    }
}
