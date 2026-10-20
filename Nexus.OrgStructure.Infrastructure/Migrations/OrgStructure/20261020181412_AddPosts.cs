using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPositions_TenantId_UserId_UnitPositionId",
                schema: "org_structure",
                table: "UserPositions");

            migrationBuilder.DropIndex(
                name: "IX_UserPositions_UnitPositionId",
                schema: "org_structure",
                table: "UserPositions");

            migrationBuilder.DropIndex(
                name: "IX_UnitPositions_TenantId_UnitId_PositionId",
                schema: "org_structure",
                table: "UnitPositions");

            migrationBuilder.AlterColumn<Guid>(
                name: "UnitId",
                schema: "org_structure",
                table: "UnitPositions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                schema: "org_structure",
                table: "UnitPositions",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "org_structure",
                table: "UnitPositions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Today's unit positions become posts: each gets its unit's company (one in a unit of no
            // company: its holder's, or it is dropped when nobody holds it), a position several
            // people hold becomes one post each, people placed directly in a company with a
            // position get a post there (the head's included), and every post a number of its
            // company as its code.
            migrationBuilder.Sql("""
                DECLARE @now datetimeoffset = CAST(SYSUTCDATETIME() AS datetimeoffset);
                DECLARE @none uniqueidentifier = '00000000-0000-0000-0000-000000000000';

                UPDATE s SET s.[CompanyId] = cu.[CompanyId]
                FROM [org_structure].[UnitPositions] s
                JOIN [org_structure].[CompanyUnits] cu ON cu.[TenantId] = s.[TenantId] AND cu.[UnitId] = s.[UnitId];

                UPDATE s SET s.[CompanyId] = x.[CompanyId]
                FROM [org_structure].[UnitPositions] s
                CROSS APPLY (SELECT TOP 1 a.[CompanyId]
                             FROM [org_structure].[UserPositions] h
                             JOIN [org_structure].[UserAssignments] a ON a.[TenantId] = h.[TenantId] AND a.[UserId] = h.[UserId]
                             WHERE h.[UnitPositionId] = s.[Id]) x
                WHERE s.[CompanyId] = @none;

                DELETE h FROM [org_structure].[UserPositions] h
                JOIN [org_structure].[UnitPositions] s ON s.[Id] = h.[UnitPositionId]
                WHERE s.[CompanyId] = @none;
                DELETE FROM [org_structure].[UnitPositions] WHERE [CompanyId] = @none;

                DECLARE @split TABLE ([HoldingId] uniqueidentifier, [PostId] uniqueidentifier);
                INSERT INTO @split
                SELECT x.[Id], NEWID()
                FROM (SELECT h.[Id], ROW_NUMBER() OVER (PARTITION BY h.[UnitPositionId] ORDER BY h.[CreatedAtUtc], h.[Id]) AS [Rn]
                      FROM [org_structure].[UserPositions] h) x
                WHERE x.[Rn] > 1;

                INSERT INTO [org_structure].[UnitPositions] ([Id], [TenantId], [CompanyId], [UnitId], [PositionId], [SortOrder], [Code], [CreatedAtUtc])
                SELECT sp.[PostId], s.[TenantId], s.[CompanyId], s.[UnitId], s.[PositionId], s.[SortOrder], N'', @now
                FROM @split sp
                JOIN [org_structure].[UserPositions] h ON h.[Id] = sp.[HoldingId]
                JOIN [org_structure].[UnitPositions] s ON s.[Id] = h.[UnitPositionId];

                UPDATE h SET h.[UnitPositionId] = sp.[PostId], h.[ModifiedAtUtc] = @now
                FROM [org_structure].[UserPositions] h
                JOIN @split sp ON sp.[HoldingId] = h.[Id];

                DECLARE @direct TABLE ([TenantId] uniqueidentifier, [UserId] uniqueidentifier, [CompanyId] uniqueidentifier,
                                       [PositionId] uniqueidentifier, [IsRoot] bit, [PostId] uniqueidentifier);
                INSERT INTO @direct
                SELECT a.[TenantId], a.[UserId], a.[CompanyId], a.[PositionId],
                       CASE WHEN p.[ParentPositionId] IS NULL THEN 1 ELSE 0 END, NEWID()
                FROM [org_structure].[UserAssignments] a
                JOIN [org_structure].[Positions] p ON p.[Id] = a.[PositionId] AND p.[TenantId] = a.[TenantId]
                WHERE a.[UnitId] IS NULL;

                INSERT INTO [org_structure].[UnitPositions] ([Id], [TenantId], [CompanyId], [UnitId], [PositionId], [SortOrder], [Code], [CreatedAtUtc])
                SELECT d.[PostId], d.[TenantId], d.[CompanyId], NULL, d.[PositionId], CASE WHEN d.[IsRoot] = 1 THEN 0 ELSE 1 END, N'', @now
                FROM @direct d;

                INSERT INTO [org_structure].[UserPositions] ([Id], [TenantId], [UserId], [UnitPositionId], [CreatedAtUtc])
                SELECT NEWID(), d.[TenantId], d.[UserId], d.[PostId], @now
                FROM @direct d;

                INSERT INTO [org_structure].[PositionCompanies] ([Id], [TenantId], [PositionId], [CompanyId], [CreatedAtUtc])
                SELECT NEWID(), x.[TenantId], x.[PositionId], x.[CompanyId], @now
                FROM (SELECT DISTINCT [TenantId], [PositionId], [CompanyId] FROM [org_structure].[UnitPositions]) x
                WHERE NOT EXISTS (SELECT 1 FROM [org_structure].[PositionCompanies] pc
                                  WHERE pc.[TenantId] = x.[TenantId] AND pc.[PositionId] = x.[PositionId] AND pc.[CompanyId] = x.[CompanyId]);

                WITH numbered AS (
                    SELECT s.[Id], ROW_NUMBER() OVER (
                               PARTITION BY s.[TenantId], s.[CompanyId]
                               ORDER BY CASE WHEN s.[UnitId] IS NULL THEN 0 ELSE 1 END, s.[UnitId], s.[SortOrder], p.[Title], s.[CreatedAtUtc], s.[Id]) AS [N]
                    FROM [org_structure].[UnitPositions] s
                    LEFT JOIN [org_structure].[Positions] p ON p.[Id] = s.[PositionId]
                )
                UPDATE s SET s.[Code] = CAST(n.[N] AS nvarchar(40))
                FROM [org_structure].[UnitPositions] s
                JOIN numbered n ON n.[Id] = s.[Id];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UserPositions_TenantId_UnitPositionId",
                schema: "org_structure",
                table: "UserPositions",
                columns: new[] { "TenantId", "UnitPositionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPositions_UserId",
                schema: "org_structure",
                table: "UserPositions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitPositions_TenantId_CompanyId_Code",
                schema: "org_structure",
                table: "UnitPositions",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitPositions_UnitId",
                schema: "org_structure",
                table: "UnitPositions",
                column: "UnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPositions_TenantId_UnitPositionId",
                schema: "org_structure",
                table: "UserPositions");

            migrationBuilder.DropIndex(
                name: "IX_UserPositions_UserId",
                schema: "org_structure",
                table: "UserPositions");

            migrationBuilder.DropIndex(
                name: "IX_UnitPositions_TenantId_CompanyId_Code",
                schema: "org_structure",
                table: "UnitPositions");

            migrationBuilder.DropIndex(
                name: "IX_UnitPositions_UnitId",
                schema: "org_structure",
                table: "UnitPositions");

            migrationBuilder.DropColumn(
                name: "Code",
                schema: "org_structure",
                table: "UnitPositions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "org_structure",
                table: "UnitPositions");

            migrationBuilder.AlterColumn<Guid>(
                name: "UnitId",
                schema: "org_structure",
                table: "UnitPositions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_UnitPositions_TenantId_UnitId_PositionId",
                schema: "org_structure",
                table: "UnitPositions",
                columns: new[] { "TenantId", "UnitId", "PositionId" },
                unique: true);
        }
    }
}
