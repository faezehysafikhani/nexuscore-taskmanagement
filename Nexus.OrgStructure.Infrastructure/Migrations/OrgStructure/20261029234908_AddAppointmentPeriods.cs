using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddAppointmentPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPositions_TenantId_UnitPositionId",
                schema: "org_structure",
                table: "UserPositions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EndDate",
                schema: "org_structure",
                table: "UserPositions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartDate",
                schema: "org_structure",
                table: "UserPositions",
                type: "datetimeoffset",
                nullable: true);

            // Today's appointments started when they were saved, and none has ended.
            migrationBuilder.Sql("UPDATE [org_structure].[UserPositions] SET [StartDate] = [CreatedAtUtc] WHERE [StartDate] IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_UserPositions_TenantId_UnitPositionId",
                schema: "org_structure",
                table: "UserPositions",
                columns: new[] { "TenantId", "UnitPositionId" },
                unique: true,
                filter: "[EndDate] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPositions_TenantId_UnitPositionId",
                schema: "org_structure",
                table: "UserPositions");

            // Back to one row per post: the ended and not yet started appointments (the history) go; the one on now stays.
            migrationBuilder.Sql("DELETE FROM [org_structure].[UserPositions] WHERE [StartDate] > SYSDATETIMEOFFSET() OR [EndDate] <= SYSDATETIMEOFFSET();");

            migrationBuilder.DropColumn(
                name: "EndDate",
                schema: "org_structure",
                table: "UserPositions");

            migrationBuilder.DropColumn(
                name: "StartDate",
                schema: "org_structure",
                table: "UserPositions");

            migrationBuilder.CreateIndex(
                name: "IX_UserPositions_TenantId_UnitPositionId",
                schema: "org_structure",
                table: "UserPositions",
                columns: new[] { "TenantId", "UnitPositionId" },
                unique: true);
        }
    }
}
