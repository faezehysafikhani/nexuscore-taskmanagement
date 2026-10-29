using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Organization.Infrastructure.Migrations.Organization
{
    /// <inheritdoc />
    public partial class AddUnitNodeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NodeTypeId",
                schema: "organization",
                table: "OrganizationUnits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentUnitId",
                schema: "organization",
                table: "OrganizationUnits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "organization",
                table: "OrganizationUnits",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidFrom",
                schema: "organization",
                table: "OrganizationUnits",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidTo",
                schema: "organization",
                table: "OrganizationUnits",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnits_NodeTypeId",
                schema: "organization",
                table: "OrganizationUnits",
                column: "NodeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUnits_ParentUnitId",
                schema: "organization",
                table: "OrganizationUnits",
                column: "ParentUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationUnits_NodeTypeId",
                schema: "organization",
                table: "OrganizationUnits");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationUnits_ParentUnitId",
                schema: "organization",
                table: "OrganizationUnits");

            migrationBuilder.DropColumn(
                name: "NodeTypeId",
                schema: "organization",
                table: "OrganizationUnits");

            migrationBuilder.DropColumn(
                name: "ParentUnitId",
                schema: "organization",
                table: "OrganizationUnits");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "organization",
                table: "OrganizationUnits");

            migrationBuilder.DropColumn(
                name: "ValidFrom",
                schema: "organization",
                table: "OrganizationUnits");

            migrationBuilder.DropColumn(
                name: "ValidTo",
                schema: "organization",
                table: "OrganizationUnits");
        }
    }
}
