using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddCompanyProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                schema: "org_structure",
                table: "Companies",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EconomicCode",
                schema: "org_structure",
                table: "Companies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                schema: "org_structure",
                table: "Companies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeadTitle",
                schema: "org_structure",
                table: "Companies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Logo",
                schema: "org_structure",
                table: "Companies",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                schema: "org_structure",
                table: "Companies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                schema: "org_structure",
                table: "Companies",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                schema: "org_structure",
                table: "Companies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                schema: "org_structure",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "EconomicCode",
                schema: "org_structure",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Email",
                schema: "org_structure",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "HeadTitle",
                schema: "org_structure",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Logo",
                schema: "org_structure",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NationalId",
                schema: "org_structure",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Phone",
                schema: "org_structure",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Website",
                schema: "org_structure",
                table: "Companies");
        }
    }
}
