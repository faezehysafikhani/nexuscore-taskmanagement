using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Organization.Infrastructure.Migrations.Organization
{
    /// <inheritdoc />
    public partial class RemoveUnitCodeParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organization");

            // Safely drop Code and ParentId columns if they exist. Use SQL with guards so operation is idempotent.
            migrationBuilder.Sql(@"
-- Drop indexes and foreign keys that reference Code (best-effort)
IF COL_LENGTH(N'organization.OrganizationUnits', N'Code') IS NOT NULL
BEGIN
    DECLARE @ixname nvarchar(128);
    SELECT TOP(1) @ixname = i.name FROM sys.indexes i
        JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID(N'organization.OrganizationUnits') AND c.name = 'Code';
    IF @ixname IS NOT NULL
        EXEC('DROP INDEX [' + @ixname + '] ON [organization].[OrganizationUnits]');

    ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [Code];
END

-- Drop any foreign key constraints referencing ParentId, then drop indexes on ParentId, then drop the ParentId column
WHILE EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc JOIN sys.columns c ON fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id WHERE c.object_id = OBJECT_ID(N'organization.OrganizationUnits') AND c.name = 'ParentId')
BEGIN
    DECLARE @fkname nvarchar(128);
    SELECT TOP(1) @fkname = fk.name FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
        JOIN sys.columns c ON fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
        WHERE c.object_id = OBJECT_ID(N'organization.OrganizationUnits') AND c.name = 'ParentId';
    IF @fkname IS NOT NULL
        EXEC('ALTER TABLE [organization].[OrganizationUnits] DROP CONSTRAINT [' + @fkname + ']');
END

WHILE EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(N'organization.OrganizationUnits') AND c.name = 'ParentId')
BEGIN
    DECLARE @ixname2 nvarchar(128);
    SELECT TOP(1) @ixname2 = i.name FROM sys.indexes i
        JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID(N'organization.OrganizationUnits') AND c.name = 'ParentId';
    IF @ixname2 IS NOT NULL
        EXEC('DROP INDEX [' + @ixname2 + '] ON [organization].[OrganizationUnits]');
END

IF COL_LENGTH(N'organization.OrganizationUnits', N'ParentId') IS NOT NULL
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [ParentId];
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organization");

            // Recreate columns if missing. Types chosen to match previous conventions; adjust if your DB used different types.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'organization.OrganizationUnits', N'Code') IS NULL
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [Code] nvarchar(40) NULL;
    -- Recreate a unique index on TenantId + Code if TenantId exists and index is missing (best-effort)
    IF COL_LENGTH(N'organization.OrganizationUnits', N'TenantId') IS NOT NULL
    BEGIN
        IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'organization.OrganizationUnits') AND name = 'IX_OrganizationUnits_TenantId_Code')
        BEGIN
            CREATE UNIQUE INDEX IX_OrganizationUnits_TenantId_Code ON [organization].[OrganizationUnits]([TenantId], [Code]);
        END
    END
END

IF COL_LENGTH(N'organization.OrganizationUnits', N'ParentId') IS NULL
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [ParentId] uniqueidentifier NULL;
    -- Note: restoring the FK relationship if desired must be done manually after inspecting data
END
");
        }
    }
}
