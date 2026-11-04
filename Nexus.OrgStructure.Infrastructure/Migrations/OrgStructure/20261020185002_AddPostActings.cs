using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddPostActings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PostActings",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacantPostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActingPostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppliedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostActings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostActings_ActingPostId",
                schema: "org_structure",
                table: "PostActings",
                column: "ActingPostId");

            migrationBuilder.CreateIndex(
                name: "IX_PostActings_TenantId_VacantPostId",
                schema: "org_structure",
                table: "PostActings",
                columns: new[] { "TenantId", "VacantPostId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostActings",
                schema: "org_structure");
        }
    }
}
