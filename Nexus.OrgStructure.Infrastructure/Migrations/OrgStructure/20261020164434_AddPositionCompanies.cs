using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddPositionCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PositionCompanies",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionCompanies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PositionCompanies_CompanyId",
                schema: "org_structure",
                table: "PositionCompanies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionCompanies_TenantId_PositionId_CompanyId",
                schema: "org_structure",
                table: "PositionCompanies",
                columns: new[] { "TenantId", "PositionId", "CompanyId" },
                unique: true);

            // 1. Each position is for the companies it is used in (its people's and its deputies'),
            //    and so are the positions above it, so a superior is for its positions' companies.
            migrationBuilder.Sql("""
                WITH used AS (
                    SELECT a.[TenantId], a.[PositionId], a.[CompanyId]
                    FROM [org_structure].[UserAssignments] a WHERE a.[PositionId] IS NOT NULL
                    UNION
                    SELECT s.[TenantId], s.[PositionId], cu.[CompanyId]
                    FROM [org_structure].[UnitPositions] s
                    JOIN [org_structure].[CompanyUnits] cu ON cu.[TenantId] = s.[TenantId] AND cu.[UnitId] = s.[UnitId]
                ),
                up AS (
                    SELECT u.[TenantId], u.[PositionId], u.[CompanyId], 0 AS [Depth] FROM used u
                    UNION ALL
                    SELECT up.[TenantId], p.[ParentPositionId], up.[CompanyId], up.[Depth] + 1
                    FROM up JOIN [org_structure].[Positions] p ON p.[Id] = up.[PositionId] AND p.[TenantId] = up.[TenantId]
                    WHERE p.[ParentPositionId] IS NOT NULL AND up.[Depth] < 50
                )
                INSERT INTO [org_structure].[PositionCompanies] ([Id], [TenantId], [PositionId], [CompanyId], [CreatedAtUtc])
                SELECT NEWID(), d.[TenantId], d.[PositionId], d.[CompanyId], CAST(SYSUTCDATETIME() AS datetimeoffset)
                FROM (SELECT DISTINCT [TenantId], [PositionId], [CompanyId] FROM up) d
                WHERE EXISTS (SELECT 1 FROM [org_structure].[Positions] p WHERE p.[Id] = d.[PositionId] AND p.[TenantId] = d.[TenantId])
                  AND EXISTS (SELECT 1 FROM [org_structure].[Companies] c WHERE c.[Id] = d.[CompanyId] AND c.[TenantId] = d.[TenantId]);
                """);

            // 2. Today's heads keep their companies, now through the head's position «رئیس کل» (one
            //    per tenant, with no superior): a head with no place, or placed directly in their
            //    company with no position, is given it there. Other heads stay as they are.
            migrationBuilder.Sql("""
                DECLARE @now datetimeoffset = CAST(SYSUTCDATETIME() AS datetimeoffset);
                DECLARE @heads TABLE ([TenantId] uniqueidentifier, [CompanyId] uniqueidentifier, [UserId] uniqueidentifier, [Placed] bit, [Seq] int);

                INSERT INTO @heads
                SELECT c.[TenantId], c.[Id], c.[ManagerUserId], CASE WHEN a.[Id] IS NULL THEN 0 ELSE 1 END,
                       ROW_NUMBER() OVER (PARTITION BY c.[TenantId], c.[ManagerUserId] ORDER BY c.[Name], c.[Id])
                FROM [org_structure].[Companies] c
                LEFT JOIN [org_structure].[UserAssignments] a ON a.[TenantId] = c.[TenantId] AND a.[UserId] = c.[ManagerUserId]
                WHERE c.[ManagerUserId] IS NOT NULL
                  AND (a.[Id] IS NULL OR (a.[CompanyId] = c.[Id] AND a.[UnitId] IS NULL AND a.[PositionId] IS NULL));

                -- Someone with no place who heads several companies is placed in the first.
                DELETE FROM @heads WHERE [Placed] = 0 AND [Seq] > 1;

                INSERT INTO [org_structure].[Positions] ([Id], [TenantId], [Title], [ParentPositionId], [IsActive], [CreatedAtUtc])
                SELECT NEWID(), t.[TenantId], N'رئیس کل', NULL, 1, @now
                FROM (SELECT DISTINCT [TenantId] FROM @heads) t
                WHERE NOT EXISTS (SELECT 1 FROM [org_structure].[Positions] p WHERE p.[TenantId] = t.[TenantId] AND p.[Title] = N'رئیس کل');

                -- A «رئیس کل» that already has a superior is not the head's: those heads stay as they are.
                DELETE h FROM @heads h
                WHERE NOT EXISTS (SELECT 1 FROM [org_structure].[Positions] p
                                  WHERE p.[TenantId] = h.[TenantId] AND p.[Title] = N'رئیس کل' AND p.[ParentPositionId] IS NULL);

                INSERT INTO [org_structure].[PositionCompanies] ([Id], [TenantId], [PositionId], [CompanyId], [CreatedAtUtc])
                SELECT NEWID(), h.[TenantId], p.[Id], h.[CompanyId], @now
                FROM @heads h
                JOIN [org_structure].[Positions] p ON p.[TenantId] = h.[TenantId] AND p.[Title] = N'رئیس کل'
                WHERE NOT EXISTS (SELECT 1 FROM [org_structure].[PositionCompanies] pc
                                  WHERE pc.[TenantId] = h.[TenantId] AND pc.[PositionId] = p.[Id] AND pc.[CompanyId] = h.[CompanyId]);

                UPDATE a SET a.[PositionId] = p.[Id], a.[ModifiedAtUtc] = @now
                FROM [org_structure].[UserAssignments] a
                JOIN @heads h ON h.[TenantId] = a.[TenantId] AND h.[UserId] = a.[UserId] AND h.[Placed] = 1
                JOIN [org_structure].[Positions] p ON p.[TenantId] = h.[TenantId] AND p.[Title] = N'رئیس کل';

                INSERT INTO [org_structure].[UserAssignments] ([Id], [TenantId], [UserId], [CompanyId], [UnitId], [PositionId], [ManagerUserId], [CreatedAtUtc])
                SELECT NEWID(), h.[TenantId], h.[UserId], h.[CompanyId], NULL, p.[Id], NULL, @now
                FROM @heads h
                JOIN [org_structure].[Positions] p ON p.[TenantId] = h.[TenantId] AND p.[Title] = N'رئیس کل'
                WHERE h.[Placed] = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PositionCompanies",
                schema: "org_structure");
        }
    }
}
