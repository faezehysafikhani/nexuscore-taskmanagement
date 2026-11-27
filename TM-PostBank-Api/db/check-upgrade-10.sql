-- Checks around db/upgrade-10-org-nodes.sql. Run it BEFORE and AFTER the upgrade and compare section A: it must be
-- identical (same rows, same Ids, same managers/parents). Section B only answers after the upgrade.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/check-upgrade-10.sql
SET NOCOUNT ON;
GO

PRINT N'== A: rows and checksums of what the structure uses (must not change)';
SELECT N'organization.OrganizationUnits' AS [Table], COUNT(*) AS [Rows], CHECKSUM_AGG(CHECKSUM([Id], [Name], [ManagerUserId], [IsActive])) AS [Checksum] FROM [organization].[OrganizationUnits]
UNION ALL SELECT N'org_structure.Companies', COUNT(*), CHECKSUM_AGG(CHECKSUM([Id], [Name], [Code], [ManagerUserId], [IsActive])) FROM [org_structure].[Companies]
UNION ALL SELECT N'org_structure.CompanyUnits', COUNT(*), CHECKSUM_AGG(CHECKSUM([Id], [CompanyId], [UnitId])) FROM [org_structure].[CompanyUnits]
UNION ALL SELECT N'org_structure.Positions', COUNT(*), CHECKSUM_AGG(CHECKSUM([Id], [Title], [ParentPositionId], [IsActive])) FROM [org_structure].[Positions]
UNION ALL SELECT N'org_structure.PositionCompanies', COUNT(*), CHECKSUM_AGG(CHECKSUM([Id], [PositionId], [CompanyId])) FROM [org_structure].[PositionCompanies]
UNION ALL SELECT N'org_structure.UnitPositions', COUNT(*), CHECKSUM_AGG(CHECKSUM([Id], [CompanyId], [UnitId], [PositionId], [Code], [ParentPostId], [SortOrder])) FROM [org_structure].[UnitPositions]
UNION ALL SELECT N'org_structure.UserPositions', COUNT(*), CHECKSUM_AGG(CHECKSUM([Id], [UserId], [UnitPositionId])) FROM [org_structure].[UserPositions]
UNION ALL SELECT N'org_structure.UserAssignments', COUNT(*), CHECKSUM_AGG(CHECKSUM([Id], [UserId], [CompanyId], [UnitId], [PositionId], [ManagerUserId])) FROM [org_structure].[UserAssignments]
UNION ALL SELECT N'org_structure.TaskPosts', COUNT(*), CHECKSUM_AGG(CHECKSUM([Id], [TaskId], [PostId], [Role])) FROM [org_structure].[TaskPosts]
UNION ALL SELECT N'org_structure.PostActings', COUNT(*), CHECKSUM_AGG(CHECKSUM([Id], [VacantPostId], [ActingPostId])) FROM [org_structure].[PostActings];
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'ParentUnitId') IS NULL OR OBJECT_ID(N'[org_structure].[NodeTypes]', N'U') IS NULL
    PRINT N'== B: upgrade-10 is not applied yet.';
ELSE
BEGIN
    PRINT N'== B: after upgrade-10 (each Problems value must be 0)';
    EXEC(N'
    SELECT N''units with a parent (flat data: 0)'' AS [Check], COUNT(*) AS [Problems] FROM [organization].[OrganizationUnits] WHERE [ParentUnitId] IS NOT NULL
    UNION ALL SELECT N''units without a node type'', COUNT(*) FROM [organization].[OrganizationUnits] WHERE [NodeTypeId] IS NULL
    UNION ALL SELECT N''organizations without exactly the 2 node types'', COUNT(*) FROM (
        SELECT t.[TenantId] FROM (SELECT [TenantId] FROM [org_structure].[Companies] UNION SELECT [TenantId] FROM [organization].[OrganizationUnits]) t
        LEFT JOIN [org_structure].[NodeTypes] n ON n.[TenantId] = t.[TenantId] GROUP BY t.[TenantId] HAVING COUNT(n.[Id]) <> 2) x
    UNION ALL SELECT N''head posts the old rule finds but are not marked'', COUNT(*) FROM [org_structure].[UnitPositions] s
        JOIN [org_structure].[Positions] p ON p.[Id] = s.[PositionId]
        WHERE s.[UnitId] IS NULL AND p.[ParentPositionId] IS NULL AND s.[IsCompanyHead] = 0
          AND EXISTS (SELECT 1 FROM [org_structure].[PositionCompanies] pc WHERE pc.[PositionId] = s.[PositionId] AND pc.[CompanyId] = s.[CompanyId])
    UNION ALL SELECT N''posts marked as manager posts (none yet)'', COUNT(*) FROM [org_structure].[UnitPositions] WHERE [IsManagerPost] = 1
    UNION ALL SELECT N''companies with a non-default screen level'', COUNT(*) FROM [org_structure].[Companies] WHERE [UiLevel] <> 0
    UNION ALL SELECT N''rules / links (none yet)'', (SELECT COUNT(*) FROM [org_structure].[NodeTypeRules]) + (SELECT COUNT(*) FROM [org_structure].[OrgLinks]);
    ');
END
GO
