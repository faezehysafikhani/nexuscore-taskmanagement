using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Organization.Infrastructure.Migrations.Organization
{
    /// <inheritdoc />
    public partial class AddUnitSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                schema: "organization",
                table: "OrganizationUnits",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                schema: "organization",
                table: "OrganizationUnits");
        }
    }
}
