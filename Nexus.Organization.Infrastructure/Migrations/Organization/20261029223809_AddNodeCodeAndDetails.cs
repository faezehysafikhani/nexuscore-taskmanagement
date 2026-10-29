using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Organization.Infrastructure.Migrations.Organization
{
    /// <inheritdoc />
    public partial class AddNodeCodeAndDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                schema: "organization",
                table: "OrganizationUnits",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrespondenceTitle",
                schema: "organization",
                table: "OrganizationUnits",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatinName",
                schema: "organization",
                table: "OrganizationUnits",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NodeCode",
                schema: "organization",
                table: "OrganizationUnits",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                schema: "organization",
                table: "OrganizationUnits",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                schema: "organization",
                table: "OrganizationUnits");

            migrationBuilder.DropColumn(
                name: "CorrespondenceTitle",
                schema: "organization",
                table: "OrganizationUnits");

            migrationBuilder.DropColumn(
                name: "LatinName",
                schema: "organization",
                table: "OrganizationUnits");

            migrationBuilder.DropColumn(
                name: "NodeCode",
                schema: "organization",
                table: "OrganizationUnits");

            migrationBuilder.DropColumn(
                name: "Phone",
                schema: "organization",
                table: "OrganizationUnits");
        }
    }
}
