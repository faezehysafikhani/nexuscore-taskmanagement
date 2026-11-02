using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.OrgStructure.Infrastructure.Migrations.OrgStructure
{
    /// <inheritdoc />
    public partial class AddOrgNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCompanyHead",
                schema: "org_structure",
                table: "UnitPositions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsManagerPost",
                schema: "org_structure",
                table: "UnitPositions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "org_structure",
                table: "UnitPositions",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentCompanyId",
                schema: "org_structure",
                table: "Companies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                schema: "org_structure",
                table: "Companies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UiLevel",
                schema: "org_structure",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AutoLinkRules",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceNodeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetNodeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoLinkRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LinkTypes",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CountsAsManager = table.Column<bool>(type: "bit", nullable: false),
                    LineStyle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NodeTypePostTemplates",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsManagerPost = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeTypePostTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NodeTypeRules",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentNodeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChildNodeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeTypeRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NodeTypes",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsDomainBoundary = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgChangeLogs",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgChangeLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgLinks",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StructureTemplateItems",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ParentKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    NodeTypeCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StructureTemplateItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StructureTemplates",
                schema: "org_structure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StructureTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ParentCompanyId",
                schema: "org_structure",
                table: "Companies",
                column: "ParentCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoLinkRules_TenantId_SourceNodeTypeId_TargetNodeTypeId_LinkTypeId",
                schema: "org_structure",
                table: "AutoLinkRules",
                columns: new[] { "TenantId", "SourceNodeTypeId", "TargetNodeTypeId", "LinkTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinkTypes_TenantId_Code",
                schema: "org_structure",
                table: "LinkTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeTypePostTemplates_NodeTypeId",
                schema: "org_structure",
                table: "NodeTypePostTemplates",
                column: "NodeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeTypeRules_ChildNodeTypeId",
                schema: "org_structure",
                table: "NodeTypeRules",
                column: "ChildNodeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeTypeRules_TenantId_ParentNodeTypeId_ChildNodeTypeId",
                schema: "org_structure",
                table: "NodeTypeRules",
                columns: new[] { "TenantId", "ParentNodeTypeId", "ChildNodeTypeId" },
                unique: true,
                filter: "[ParentNodeTypeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NodeTypes_TenantId_Code",
                schema: "org_structure",
                table: "NodeTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgChangeLogs_TenantId_EntityId_AtUtc",
                schema: "org_structure",
                table: "OrgChangeLogs",
                columns: new[] { "TenantId", "EntityId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgLinks_TenantId_FromUnitId_ToUnitId_LinkTypeId",
                schema: "org_structure",
                table: "OrgLinks",
                columns: new[] { "TenantId", "FromUnitId", "ToUnitId", "LinkTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgLinks_ToUnitId",
                schema: "org_structure",
                table: "OrgLinks",
                column: "ToUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StructureTemplateItems_TenantId_TemplateId_Key",
                schema: "org_structure",
                table: "StructureTemplateItems",
                columns: new[] { "TenantId", "TemplateId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StructureTemplates_TenantId_Code",
                schema: "org_structure",
                table: "StructureTemplates",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            // Today's data keeps its meaning: every organization gets the node types «شرکت» and «واحد»,
            // its units become plain «واحد» nodes (no parent: directly under the company's implicit
            // root), and the head's post of each company (directly in it, of the position with no
            // superior) gets the head's mark. No rule is added, so a company stays flat.
            migrationBuilder.Sql(@"
CREATE TABLE #OrgNodeTenants (TenantId uniqueidentifier NOT NULL PRIMARY KEY);
INSERT INTO #OrgNodeTenants SELECT DISTINCT [TenantId] FROM [org_structure].[Companies];
IF OBJECT_ID(N'[organization].[OrganizationUnits]', N'U') IS NOT NULL
    EXEC(N'INSERT INTO #OrgNodeTenants SELECT DISTINCT u.[TenantId] FROM [organization].[OrganizationUnits] u WHERE NOT EXISTS (SELECT 1 FROM #OrgNodeTenants t WHERE t.[TenantId] = u.[TenantId])');

INSERT INTO [org_structure].[NodeTypes] ([Id], [TenantId], [Code], [Name], [IsDomainBoundary], [SortOrder], [IsActive], [CreatedAtUtc])
SELECT NEWID(), t.[TenantId], v.[Code], v.[Name], 1, v.[SortOrder], 1, SYSDATETIMEOFFSET()
FROM #OrgNodeTenants t
CROSS JOIN (VALUES (N'company', N'شرکت', 0), (N'unit', N'واحد', 1)) v([Code], [Name], [SortOrder])
WHERE NOT EXISTS (SELECT 1 FROM [org_structure].[NodeTypes] n WHERE n.[TenantId] = t.[TenantId] AND n.[Code] = v.[Code]);

IF COL_LENGTH(N'organization.OrganizationUnits', N'NodeTypeId') IS NOT NULL
    EXEC(N'UPDATE u SET u.[NodeTypeId] = n.[Id] FROM [organization].[OrganizationUnits] u JOIN [org_structure].[NodeTypes] n ON n.[TenantId] = u.[TenantId] AND n.[Code] = N''unit'' WHERE u.[NodeTypeId] IS NULL');

UPDATE s SET s.[IsCompanyHead] = 1
FROM [org_structure].[UnitPositions] s
JOIN [org_structure].[Positions] p ON p.[Id] = s.[PositionId]
WHERE s.[UnitId] IS NULL AND p.[ParentPositionId] IS NULL AND s.[IsCompanyHead] = 0
  AND EXISTS (SELECT 1 FROM [org_structure].[PositionCompanies] pc WHERE pc.[PositionId] = s.[PositionId] AND pc.[CompanyId] = s.[CompanyId]);

DROP TABLE #OrgNodeTenants;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoLinkRules",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "LinkTypes",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "NodeTypePostTemplates",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "NodeTypeRules",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "NodeTypes",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "OrgChangeLogs",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "OrgLinks",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "StructureTemplateItems",
                schema: "org_structure");

            migrationBuilder.DropTable(
                name: "StructureTemplates",
                schema: "org_structure");

            migrationBuilder.DropIndex(
                name: "IX_Companies_ParentCompanyId",
                schema: "org_structure",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "IsCompanyHead",
                schema: "org_structure",
                table: "UnitPositions");

            migrationBuilder.DropColumn(
                name: "IsManagerPost",
                schema: "org_structure",
                table: "UnitPositions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "org_structure",
                table: "UnitPositions");

            migrationBuilder.DropColumn(
                name: "ParentCompanyId",
                schema: "org_structure",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                schema: "org_structure",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "UiLevel",
                schema: "org_structure",
                table: "Companies");
        }
    }
}
