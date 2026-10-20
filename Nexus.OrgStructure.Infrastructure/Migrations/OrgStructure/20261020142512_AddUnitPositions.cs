using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddUnitPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnitPositions",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitPositions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPositions",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitPositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPositions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnitPositions_PositionId",
                schema: "org_structure",
                table: "UnitPositions",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitPositions_TenantId_UnitId_PositionId",
                schema: "org_structure",
                table: "UnitPositions",
                columns: new[] { "TenantId", "UnitId", "PositionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPositions_TenantId_UserId_UnitPositionId",
                schema: "org_structure",
                table: "UserPositions",
                columns: new[] { "TenantId", "UserId", "UnitPositionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPositions_UnitPositionId",
                schema: "org_structure",
                table: "UserPositions",
                column: "UnitPositionId");

            // Today's places become unit positions: each unit gets the positions its people have
            // (in title order), and each person holds their position there.
            migrationBuilder.Sql("""
                INSERT INTO [org_structure].[UnitPositions] ([Id], [TenantId], [UnitId], [PositionId], [SortOrder], [CreatedAtUtc])
                SELECT NEWID(), d.[TenantId], d.[UnitId], d.[PositionId],
                       ROW_NUMBER() OVER (PARTITION BY d.[TenantId], d.[UnitId] ORDER BY p.[Title]) - 1,
                       CAST(SYSUTCDATETIME() AS datetimeoffset)
                FROM (SELECT DISTINCT [TenantId], [UnitId], [PositionId] FROM [org_structure].[UserAssignments]
                      WHERE [UnitId] IS NOT NULL AND [PositionId] IS NOT NULL) d
                JOIN [org_structure].[Positions] p ON p.[Id] = d.[PositionId];

                INSERT INTO [org_structure].[UserPositions] ([Id], [TenantId], [UserId], [UnitPositionId], [CreatedAtUtc])
                SELECT NEWID(), a.[TenantId], a.[UserId], s.[Id], CAST(SYSUTCDATETIME() AS datetimeoffset)
                FROM [org_structure].[UserAssignments] a
                JOIN [org_structure].[UnitPositions] s
                  ON s.[TenantId] = a.[TenantId] AND s.[UnitId] = a.[UnitId] AND s.[PositionId] = a.[PositionId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnitPositions",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "UserPositions",
                schema: "org_structure");
        }
    }
}
