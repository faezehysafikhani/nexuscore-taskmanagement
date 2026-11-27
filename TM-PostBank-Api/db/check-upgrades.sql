-- Which of db/upgrade-1 .. upgrade-19 a database already has: read-only, changes nothing.
-- Each step is recognised by what it adds (a table or column), so a database created whole by a newer
-- version (no history rows) answers correctly too. Run the missing steps in order, from the first "no".
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/check-upgrades.sql
SET NOCOUNT ON;
GO

DECLARE @steps TABLE ([Step] int, [Script] nvarchar(60), [Applied] bit, [Recognised by] nvarchar(200));
DECLARE @hasUnits bit = CASE WHEN OBJECT_ID(N'[organization].[OrganizationUnits]', N'U') IS NOT NULL THEN 1 ELSE 0 END;

INSERT INTO @steps VALUES
 (1, N'upgrade-1-organization.sql',
     CASE WHEN @hasUnits = 1 AND COL_LENGTH(N'organization.OrganizationUnits', N'Code') IS NULL
               AND COL_LENGTH(N'organization.OrganizationUnits', N'ParentId') IS NULL THEN 1 ELSE 0 END,
     N'OrganizationUnits without the old Code / ParentId columns'),
 (2, N'upgrade-2-org-structure.sql',
     CASE WHEN COL_LENGTH(N'org_structure.Positions', N'ParentPositionId') IS NOT NULL
               AND OBJECT_ID(N'[org_structure].[Honorifics]', N'U') IS NOT NULL THEN 1 ELSE 0 END,
     N'Positions.ParentPositionId and the Honorifics table'),
 (3, N'upgrade-3-unit-positions.sql',
     CASE WHEN OBJECT_ID(N'[org_structure].[UnitPositions]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[org_structure].[UserPositions]', N'U') IS NOT NULL THEN 1 ELSE 0 END,
     N'UnitPositions and UserPositions tables'),
 (4, N'upgrade-4-position-companies.sql',
     CASE WHEN OBJECT_ID(N'[org_structure].[PositionCompanies]', N'U') IS NOT NULL THEN 1 ELSE 0 END,
     N'PositionCompanies table'),
 (5, N'upgrade-5-posts.sql',
     CASE WHEN COL_LENGTH(N'org_structure.UnitPositions', N'Code') IS NOT NULL
               AND COL_LENGTH(N'org_structure.UnitPositions', N'CompanyId') IS NOT NULL THEN 1 ELSE 0 END,
     N'UnitPositions.Code and UnitPositions.CompanyId'),
 (6, N'upgrade-6-task-posts.sql',
     CASE WHEN OBJECT_ID(N'[org_structure].[TaskPosts]', N'U') IS NOT NULL THEN 1 ELSE 0 END,
     N'TaskPosts table'),
 (7, N'upgrade-7-substitution.sql',
     CASE WHEN OBJECT_ID(N'[org_structure].[PostActings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'task_management.Tasks', N'OwnerHeldByPost') IS NOT NULL
               AND COL_LENGTH(N'task_management.TaskAssignees', N'IsActing') IS NOT NULL THEN 1 ELSE 0 END,
     N'PostActings table, Tasks.OwnerHeldByPost, TaskAssignees.IsActing'),
 (8, N'upgrade-8-post-parent.sql',
     CASE WHEN COL_LENGTH(N'org_structure.UnitPositions', N'ParentPostId') IS NOT NULL THEN 1 ELSE 0 END,
     N'UnitPositions.ParentPostId'),
 (9, N'upgrade-9-task-history.sql',
     CASE WHEN OBJECT_ID(N'[org_structure].[TaskPostPeriods]', N'U') IS NOT NULL THEN 1 ELSE 0 END,
     N'TaskPostPeriods table'),
 (10, N'upgrade-10-org-nodes.sql',
     CASE WHEN COL_LENGTH(N'organization.OrganizationUnits', N'ParentUnitId') IS NOT NULL
               AND OBJECT_ID(N'[org_structure].[NodeTypes]', N'U') IS NOT NULL
               AND COL_LENGTH(N'org_structure.UnitPositions', N'IsCompanyHead') IS NOT NULL THEN 1 ELSE 0 END,
     N'OrganizationUnits.ParentUnitId, NodeTypes table, UnitPositions.IsCompanyHead'),
 (11, N'upgrade-11-node-api.sql',
     CASE WHEN COL_LENGTH(N'organization.OrganizationUnits', N'SortOrder') IS NOT NULL
               AND COL_LENGTH(N'org_structure.NodeTypes', N'Color') IS NOT NULL
               AND COL_LENGTH(N'org_structure.NodeTypes', N'Icon') IS NOT NULL THEN 1 ELSE 0 END,
     N'OrganizationUnits.SortOrder, NodeTypes.Color and NodeTypes.Icon'),
 (12, N'upgrade-12-templates.sql',
     CASE WHEN COL_LENGTH(N'org_structure.StructureTemplates', N'Content') IS NOT NULL THEN 1 ELSE 0 END,
     N'StructureTemplates.Content'),
 (13, N'upgrade-13-node-details.sql',
     CASE WHEN COL_LENGTH(N'organization.OrganizationUnits', N'NodeCode') IS NOT NULL
               AND COL_LENGTH(N'organization.OrganizationUnits', N'Address') IS NOT NULL THEN 1 ELSE 0 END,
     N'OrganizationUnits.NodeCode ... Address'),
 (14, N'upgrade-14-appointments.sql',
     CASE WHEN COL_LENGTH(N'org_structure.UserPositions', N'StartDate') IS NOT NULL
               AND COL_LENGTH(N'org_structure.UserPositions', N'EndDate') IS NOT NULL THEN 1 ELSE 0 END,
     N'UserPositions.StartDate and EndDate'),
 (15, N'upgrade-15-company-profile.sql',
     CASE WHEN COL_LENGTH(N'org_structure.Companies', N'HeadTitle') IS NOT NULL
               AND COL_LENGTH(N'org_structure.Companies', N'Logo') IS NOT NULL THEN 1 ELSE 0 END,
     N'Companies.Phone ... Logo'),
 (16, N'upgrade-16-user-personal-details.sql',
     CASE WHEN COL_LENGTH(N'identity.Users', N'BirthDate') IS NOT NULL THEN 1 ELSE 0 END,
     N'Users.Address, FatherName, BirthDate'),
 (17, N'upgrade-17-stand-in-period.sql',
     CASE WHEN COL_LENGTH(N'org_structure.PostActings', N'EndDate') IS NOT NULL THEN 1 ELSE 0 END,
     N'PostActings.StartDate and EndDate'),
 (18, N'upgrade-18-calendar-meetings.sql',
     CASE WHEN OBJECT_ID(N'calendar.Meetings', N'U') IS NOT NULL AND OBJECT_ID(N'calendar.MeetingParticipants', N'U') IS NOT NULL THEN 1 ELSE 0 END,
     N'calendar.Meetings and MeetingParticipants'),
 (19, N'upgrade-19-position-permissions.sql',
     CASE WHEN OBJECT_ID(N'org_structure.PositionPermissions', N'U') IS NOT NULL AND OBJECT_ID(N'org_structure.UnitPositionPermissions', N'U') IS NOT NULL THEN 1 ELSE 0 END,
     N'org_structure.PositionPermissions and UnitPositionPermissions');

PRINT N'== Upgrade steps';
SELECT [Step], [Script], CASE [Applied] WHEN 1 THEN N'yes' ELSE N'NO' END AS [Applied], [Recognised by] FROM @steps ORDER BY [Step];

DECLARE @firstMissing int = (SELECT MIN([Step]) FROM @steps WHERE [Applied] = 0);
DECLARE @lastApplied int = (SELECT MAX([Step]) FROM @steps WHERE [Applied] = 1);
IF @firstMissing IS NULL
    PRINT N'== Result: up to date (all 19 steps applied).';
ELSE IF @lastApplied > @firstMissing
    PRINT N'== Result: steps are out of order - step ' + CAST(@firstMissing AS nvarchar(5)) + N' is missing but a later one is applied. Check before running anything.';
ELSE
    PRINT N'== Result: run from upgrade-' + CAST(@firstMissing AS nvarchar(5)) + N' on, in order.';

IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NOT NULL
BEGIN
    PRINT N'== The last migrations recorded (for reference)';
    SELECT TOP 5 [MigrationId], [ProductVersion] FROM [dbo].[__EFMigrationsHistory] ORDER BY [MigrationId] DESC;
END
GO
