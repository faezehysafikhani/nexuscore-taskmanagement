/*
  دادهٔ نمونهٔ حجیم برای سرور آزمایشی TMTejarat-API (SQL Server).

  ⚠ این اسکریپت ابتدا «همه‌چیز» را پاک می‌کند، به‌جز مدیر سیستم (admin)، و بعد دادهٔ نمونه می‌سازد.
  ⚠ فقط روی دیتابیس آزمایشی. روی دیتابیس واقعی اجرا نکنید. پیش از اجرا بکاپ بگیرید.

  چه چیزی پاک می‌شود:
    کاربران (جز admin) با نقش‌ها، دسترسی‌های مستقیم، نشست‌ها و توکن‌ها؛ گروه‌ها/تیم‌ها؛ گزارش فعالیت‌ها؛
    همهٔ جدول‌های task_management و organization؛ داده‌های org_structure (شرکت، سمت، پست، انتصاب، قالب‌ها،
    لینک‌ها، تاریخچه)؛ جلسه‌ها؛ نمونه‌ها و تصمیم‌های گردش‌کار؛ رویدادها؛ اعلان‌ها؛ تیکت‌ها؛ گفتگوها.
  چه چیزی می‌ماند (تنظیمات و فهرست‌های پایه):
    admin و نقش‌هایش، Tenants، Permissions، Roles و RolePermissions، platform.Settings،
    نوع‌های گره/لینک‌ها/قاعده‌ها/عنوان‌ها در org_structure، تقویم‌های کاری، تعریف‌های گردش‌کار.

  چه چیزی ساخته می‌شود:
    ۱۸ نفر مشخص + @StaffPerTeam نفر برای هر تیم (۵ تیم)، رمز همه Test@Pass#2026؛ دو نقش (کارمند، مدیر واحد)؛
    ۵ تیم؛ شرکت «شرکت فناوران پارس» با ۵ واحد، سمت‌ها، پست‌ها و انتصاب‌ها؛ برچسب‌ها؛ فعالیت‌ها
    (حدود ۱۳۰ عنوان آماده + @TasksPerPerson فعالیت برای هر نفر) با مسئول، دسترسی تیم، برچسب و
    کامنت (بعضی عقب‌افتاده)؛ ۱۰ پروژه با زیرفعالیت؛ یادداشت‌های شخصی.
  ساخته نمی‌شود: جلسه، گفتگو، ساعت کار، درخواست (برای این‌ها از seed-demo-data.mjs یا خود برنامه استفاده کنید).

  همه‌چیز در یک تراکنش است: هر خطایی کل کار را برمی‌گرداند و دیتابیس همان‌طور که بود می‌ماند.
  پیش از پاک‌کردن، ستون‌های لازم همهٔ جدول‌ها با دیتابیس شما سنجیده می‌شود؛ اگر نسخهٔ دیتابیس با این اسکریپت
  نمی‌خواند، با فهرست ستون‌ها متوقف می‌شود و چیزی پاک نمی‌شود.

  اجرا (SSMS یا sqlcmd؛ UTF-8):
    ۱) @ConfirmWipe را پایین به N'YES' تغییر دهید.
    ۲) sqlcmd -S <server> -d <database> -I -f 65001 -i db/seed-demo-data.sql
  بعد از اجرا: برنامه را restart کنید (حافظهٔ پنهان ساختار سازمانی/دسترسی‌ها) و admin دوباره وارد شود.
  برای ورود هر کاربر: نام کاربری (کد ملی) را از خروجی انتهای اسکریپت بردارید؛ رمز Test@Pass#2026.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- A run stopped half-way leaves its temp tables in the session; clear them so it can run again.
DROP TABLE IF EXISTS #Client;
DROP TABLE IF EXISTS #Comment;
DROP TABLE IF EXISTS #Curated;
DROP TABLE IF EXISTS #First;
DROP TABLE IF EXISTS #Fk;
DROP TABLE IF EXISTS #GDesc;
DROP TABLE IF EXISTS #Job;
DROP TABLE IF EXISTS #Last;
DROP TABLE IF EXISTS #Note;
DROP TABLE IF EXISTS #Num;
DROP TABLE IF EXISTS #Obj;
DROP TABLE IF EXISTS #People;
DROP TABLE IF EXISTS #PermName;
DROP TABLE IF EXISTS #Person;
DROP TABLE IF EXISTS #Post;
DROP TABLE IF EXISTS #Prob;
DROP TABLE IF EXISTS #Proj;
DROP TABLE IF EXISTS #Seed;
DROP TABLE IF EXISTS #Staff;
DROP TABLE IF EXISTS #Step;
DROP TABLE IF EXISTS #Supplied;
DROP TABLE IF EXISTS #Tag;
DROP TABLE IF EXISTS #Task;
DROP TABLE IF EXISTS #Team;
DROP TABLE IF EXISTS #TeamManager;
DROP TABLE IF EXISTS #TeamManagerRaw;
DROP TABLE IF EXISTS #Touched;
DROP TABLE IF EXISTS #Verb;
DROP TABLE IF EXISTS #Wipe;
SET QUOTED_IDENTIFIER ON;

------------------------------------------------------------------------------------------------ settings
DECLARE @ConfirmWipe nvarchar(3) = N'NO';   -- ⚠ برای اجرا N'YES' بگذارید
DECLARE @StaffPerTeam int = 20;             -- کاربر تولیدشده برای هر تیم (علاوه بر ۱۸ نفر مشخص)
DECLARE @TasksPerPerson int = 10;           -- فعالیت تولیدشده برای هر نفر غیرمدیر
DECLARE @PwHash nvarchar(256) = N'$2a$12$smzipACZxBqJ61MCLVGP4.fJfklczoxnQayxbtXuo3oP7rchAnpo2';   -- bcrypt (cost 12) of Test@Pass#2026

IF @ConfirmWipe <> N'YES'
BEGIN
    PRINT N'Nothing was done: set @ConfirmWipe to N''YES'' at the top of the script. It deletes all data except the administrator.';
    PRINT N'Database: ' + DB_NAME() + N'   Server: ' + @@SERVERNAME;
    RETURN;
END;

PRINT N'Database: ' + DB_NAME() + N'   Server: ' + @@SERVERNAME;

------------------------------------------------------------------------------------------------ preflight
-- Every column this script writes, and every required column it does not write, is checked BEFORE anything is deleted.
CREATE TABLE #Supplied (Tbl nvarchar(200) NOT NULL, Col sysname NOT NULL);
INSERT INTO #Supplied (Tbl, Col) VALUES
    (N'identity.Roles', N'Id'),
    (N'identity.Roles', N'TenantId'),
    (N'identity.Roles', N'Name'),
    (N'identity.Roles', N'NormalizedName'),
    (N'identity.Roles', N'Description'),
    (N'identity.Roles', N'IsSystem'),
    (N'identity.Roles', N'CreatedAtUtc'),
    (N'identity.Roles', N'CreatedByUserId'),
    (N'identity.RolePermissions', N'RoleId'),
    (N'identity.RolePermissions', N'PermissionId'),
    (N'identity.Users', N'Id'),
    (N'identity.Users', N'TenantId'),
    (N'identity.Users', N'Username'),
    (N'identity.Users', N'FirstName'),
    (N'identity.Users', N'LastName'),
    (N'identity.Users', N'DisplayName'),
    (N'identity.Users', N'Email'),
    (N'identity.Users', N'PhoneNumber'),
    (N'identity.Users', N'PasswordHash'),
    (N'identity.Users', N'IsActive'),
    (N'identity.Users', N'IsSystem'),
    (N'identity.Users', N'NotifySms'),
    (N'identity.Users', N'SmsOnEveryNewSignIn'),
    (N'identity.Users', N'Address'),
    (N'identity.Users', N'FatherName'),
    (N'identity.Users', N'BirthDate'),
    (N'identity.Users', N'CreatedAtUtc'),
    (N'identity.Users', N'CreatedByUserId'),
    (N'identity.UserRoles', N'UserId'),
    (N'identity.UserRoles', N'RoleId'),
    (N'identity.UserGroups', N'Id'),
    (N'identity.UserGroups', N'TenantId'),
    (N'identity.UserGroups', N'Name'),
    (N'identity.UserGroups', N'NormalizedName'),
    (N'identity.UserGroups', N'Description'),
    (N'identity.UserGroups', N'IsActive'),
    (N'identity.UserGroups', N'OwnerUserId'),
    (N'identity.UserGroups', N'CreatedAtUtc'),
    (N'identity.UserGroups', N'CreatedByUserId'),
    (N'identity.UserGroupMembers', N'UserGroupId'),
    (N'identity.UserGroupMembers', N'UserId'),
    (N'identity.UserGroupMembers', N'JoinedAtUtc'),
    (N'org_structure.Companies', N'Id'),
    (N'org_structure.Companies', N'TenantId'),
    (N'org_structure.Companies', N'Name'),
    (N'org_structure.Companies', N'Code'),
    (N'org_structure.Companies', N'Description'),
    (N'org_structure.Companies', N'ManagerUserId'),
    (N'org_structure.Companies', N'IsActive'),
    (N'org_structure.Companies', N'UiLevel'),
    (N'org_structure.Companies', N'CreatedAtUtc'),
    (N'org_structure.Companies', N'CreatedByUserId'),
    (N'org_structure.Positions', N'Id'),
    (N'org_structure.Positions', N'TenantId'),
    (N'org_structure.Positions', N'Title'),
    (N'org_structure.Positions', N'ParentPositionId'),
    (N'org_structure.Positions', N'IsActive'),
    (N'org_structure.Positions', N'CreatedAtUtc'),
    (N'org_structure.Positions', N'CreatedByUserId'),
    (N'org_structure.PositionCompanies', N'Id'),
    (N'org_structure.PositionCompanies', N'TenantId'),
    (N'org_structure.PositionCompanies', N'PositionId'),
    (N'org_structure.PositionCompanies', N'CompanyId'),
    (N'org_structure.PositionCompanies', N'CreatedAtUtc'),
    (N'org_structure.PositionCompanies', N'CreatedByUserId'),
    (N'organization.OrganizationUnits', N'Id'),
    (N'organization.OrganizationUnits', N'TenantId'),
    (N'organization.OrganizationUnits', N'Name'),
    (N'organization.OrganizationUnits', N'ManagerUserId'),
    (N'organization.OrganizationUnits', N'IsActive'),
    (N'organization.OrganizationUnits', N'NodeTypeId'),
    (N'organization.OrganizationUnits', N'SortOrder'),
    (N'organization.OrganizationUnits', N'CreatedAtUtc'),
    (N'organization.OrganizationUnits', N'CreatedByUserId'),
    (N'org_structure.CompanyUnits', N'Id'),
    (N'org_structure.CompanyUnits', N'TenantId'),
    (N'org_structure.CompanyUnits', N'CompanyId'),
    (N'org_structure.CompanyUnits', N'UnitId'),
    (N'org_structure.CompanyUnits', N'CreatedAtUtc'),
    (N'org_structure.CompanyUnits', N'CreatedByUserId'),
    (N'org_structure.UnitPositions', N'Id'),
    (N'org_structure.UnitPositions', N'TenantId'),
    (N'org_structure.UnitPositions', N'UnitId'),
    (N'org_structure.UnitPositions', N'PositionId'),
    (N'org_structure.UnitPositions', N'SortOrder'),
    (N'org_structure.UnitPositions', N'Code'),
    (N'org_structure.UnitPositions', N'CompanyId'),
    (N'org_structure.UnitPositions', N'ParentPostId'),
    (N'org_structure.UnitPositions', N'IsCompanyHead'),
    (N'org_structure.UnitPositions', N'IsManagerPost'),
    (N'org_structure.UnitPositions', N'CreatedAtUtc'),
    (N'org_structure.UnitPositions', N'CreatedByUserId'),
    (N'org_structure.UserAssignments', N'Id'),
    (N'org_structure.UserAssignments', N'TenantId'),
    (N'org_structure.UserAssignments', N'UserId'),
    (N'org_structure.UserAssignments', N'CompanyId'),
    (N'org_structure.UserAssignments', N'UnitId'),
    (N'org_structure.UserAssignments', N'PositionId'),
    (N'org_structure.UserAssignments', N'ManagerUserId'),
    (N'org_structure.UserAssignments', N'CreatedAtUtc'),
    (N'org_structure.UserAssignments', N'CreatedByUserId'),
    (N'org_structure.UserPositions', N'Id'),
    (N'org_structure.UserPositions', N'TenantId'),
    (N'org_structure.UserPositions', N'UserId'),
    (N'org_structure.UserPositions', N'UnitPositionId'),
    (N'org_structure.UserPositions', N'CreatedAtUtc'),
    (N'org_structure.UserPositions', N'CreatedByUserId'),
    (N'task_management.Tags', N'Id'),
    (N'task_management.Tags', N'TenantId'),
    (N'task_management.Tags', N'Name'),
    (N'task_management.Tags', N'NormalizedName'),
    (N'task_management.Tags', N'Color'),
    (N'task_management.Tags', N'CreatedAtUtc'),
    (N'task_management.Tags', N'CreatedByUserId'),
    (N'task_management.Tasks', N'Id'),
    (N'task_management.Tasks', N'TenantId'),
    (N'task_management.Tasks', N'Title'),
    (N'task_management.Tasks', N'Description'),
    (N'task_management.Tasks', N'DueDate'),
    (N'task_management.Tasks', N'DueTime'),
    (N'task_management.Tasks', N'IsProject'),
    (N'task_management.Tasks', N'Priority'),
    (N'task_management.Tasks', N'Status'),
    (N'task_management.Tasks', N'OwnerUserId'),
    (N'task_management.Tasks', N'OwnerHeldByPost'),
    (N'task_management.Tasks', N'AssignedUserId'),
    (N'task_management.Tasks', N'AssignedUserGroupId'),
    (N'task_management.Tasks', N'AllowAssigneeStatusUpdate'),
    (N'task_management.Tasks', N'ReviewStatus'),
    (N'task_management.Tasks', N'ActualCompletionDateUtc'),
    (N'task_management.Tasks', N'CreatedAtUtc'),
    (N'task_management.Tasks', N'CreatedByUserId'),
    (N'task_management.Tasks', N'ModifiedAtUtc'),
    (N'task_management.Tasks', N'ModifiedByUserId'),
    (N'task_management.TaskAssignees', N'Id'),
    (N'task_management.TaskAssignees', N'TaskId'),
    (N'task_management.TaskAssignees', N'UserId'),
    (N'task_management.TaskAssignees', N'IsResponsible'),
    (N'task_management.TaskAssignees', N'IsActing'),
    (N'task_management.TaskTags', N'Id'),
    (N'task_management.TaskTags', N'TaskId'),
    (N'task_management.TaskTags', N'TagId'),
    (N'task_management.TaskComments', N'Id'),
    (N'task_management.TaskComments', N'TenantId'),
    (N'task_management.TaskComments', N'TaskId'),
    (N'task_management.TaskComments', N'UserId'),
    (N'task_management.TaskComments', N'Text'),
    (N'task_management.TaskComments', N'CreatedAtUtc'),
    (N'task_management.TaskComments', N'CreatedByUserId'),
    (N'task_management.Tasks', N'CharterDescription'),
    (N'task_management.Tasks', N'CharterProjectManager'),
    (N'task_management.Tasks', N'CharterStartDate'),
    (N'task_management.Tasks', N'CharterEndDate'),
    (N'task_management.SubTasks', N'Id'),
    (N'task_management.SubTasks', N'TenantId'),
    (N'task_management.SubTasks', N'TaskId'),
    (N'task_management.SubTasks', N'Title'),
    (N'task_management.SubTasks', N'Importance'),
    (N'task_management.SubTasks', N'SortOrder'),
    (N'task_management.SubTasks', N'StartDate'),
    (N'task_management.SubTasks', N'EndDate'),
    (N'task_management.SubTasks', N'IsCompleted'),
    (N'task_management.SubTasks', N'IsGeneratedOccurrence'),
    (N'task_management.SubTasks', N'Status'),
    (N'task_management.SubTasks', N'ActualStartedAtUtc'),
    (N'task_management.SubTasks', N'ActualCompletedAtUtc'),
    (N'task_management.SubTasks', N'CreatedAtUtc'),
    (N'task_management.SubTasks', N'CreatedByUserId'),
    (N'task_management.SubTaskAssignees', N'Id'),
    (N'task_management.SubTaskAssignees', N'SubTaskId'),
    (N'task_management.SubTaskAssignees', N'UserId'),
    (N'task_management.Notes', N'Id'),
    (N'task_management.Notes', N'TenantId'),
    (N'task_management.Notes', N'UserId'),
    (N'task_management.Notes', N'Title'),
    (N'task_management.Notes', N'Content'),
    (N'task_management.Notes', N'Color'),
    (N'task_management.Notes', N'IsPinned'),
    (N'task_management.Notes', N'CreatedAtUtc'),
    (N'task_management.Notes', N'CreatedByUserId');

CREATE TABLE #Prob (Id int IDENTITY(1, 1) PRIMARY KEY, Problem nvarchar(500) NOT NULL);
INSERT INTO #Prob (Problem)
SELECT N'table ' + s.Tbl + N' does not exist'
FROM (SELECT DISTINCT Tbl FROM #Supplied) s
WHERE OBJECT_ID(s.Tbl, N'U') IS NULL;

INSERT INTO #Prob (Problem)
SELECT N'column ' + s.Tbl + N'.' + s.Col + N' does not exist'
FROM #Supplied s
WHERE OBJECT_ID(s.Tbl, N'U') IS NOT NULL AND COL_LENGTH(s.Tbl, s.Col) IS NULL;

INSERT INTO #Prob (Problem)
SELECT N'column ' + t.Tbl + N'.' + c.name + N' is required in your database and this script does not fill it (' + TYPE_NAME(c.user_type_id) + N')'
FROM (SELECT DISTINCT Tbl FROM #Supplied) t
JOIN sys.columns c ON c.object_id = OBJECT_ID(t.Tbl, N'U')
WHERE c.is_nullable = 0 AND c.is_identity = 0 AND c.is_computed = 0 AND c.default_object_id = 0 AND c.system_type_id <> 189
  AND NOT EXISTS (SELECT 1 FROM #Supplied x WHERE x.Tbl = t.Tbl AND x.Col = c.name);

IF EXISTS (SELECT 1 FROM #Prob)
BEGIN
    DECLARE @Problems nvarchar(max) = N'';
    SELECT @Problems += N' - ' + Problem + NCHAR(10) FROM #Prob ORDER BY Id;
    SELECT Problem AS [This script does not match your database] FROM #Prob ORDER BY Id;   -- also shown in the Results tab
    PRINT N'Stopped before changing anything. This script does not match your database:';
    PRINT @Problems;
    RAISERROR(N'The script does not match the database schema. Nothing was changed. Problems: %s', 16, 1, @Problems);
    RETURN;
END;

DECLARE @Admin uniqueidentifier =
    (SELECT TOP (1) Id FROM [identity].[Users]
     WHERE IsSystem = 1 OR Id = '33333333-3333-3333-3333-333333333333'
     ORDER BY IsSystem DESC, CreatedAtUtc);
IF @Admin IS NULL
BEGIN
    RAISERROR(N'The built-in administrator was not found (identity.Users, IsSystem = 1). Nothing was changed.', 16, 1);
    RETURN;
END;
DECLARE @Tenant uniqueidentifier = (SELECT TenantId FROM [identity].[Users] WHERE Id = @Admin);
DECLARE @Now datetimeoffset = SYSDATETIMEOFFSET();
DECLARE @Today date = CAST(SYSDATETIMEOFFSET() AT TIME ZONE N'Iran Standard Time' AS date);
PRINT N'Administrator kept: ' + CAST(@Admin AS nvarchar(40));

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------ wipe
CREATE TABLE #Wipe (Sch sysname NOT NULL, Tbl sysname NOT NULL);
INSERT INTO #Wipe (Sch, Tbl)
SELECT s.name, t.name
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name IN (N'task_management', N'organization')
   OR (s.name = N'org_structure' AND t.name NOT IN (N'NodeTypes', N'NodeTypeRules', N'LinkTypes', N'AutoLinkRules', N'Honorifics'))
   OR (s.name = N'calendar' AND t.name IN (N'Meetings', N'MeetingParticipants'))
   OR (s.name = N'workflow' AND t.name IN (N'WorkflowInstances', N'WorkflowDecisions', N'WorkflowDelegations'))
   OR (s.name = N'events' AND t.name = N'Events')
   OR (s.name = N'platform' AND t.name = N'AuditLogs')
   OR (s.name = N'identity' AND t.name IN (N'UserGroupMembers', N'UserGroupPermissions', N'UserGroups'))
   OR (s.name = N'dbo' AND t.name IN (N'Notifications', N'Tickets', N'TicketComments', N'Conversations', N'ConversationParticipants', N'Messages', N'MessageReads'));

-- Tables of people: only the rows of other people go.
CREATE TABLE #People (Tbl sysname NOT NULL);
INSERT INTO #People (Tbl) VALUES (N'UserRoles'), (N'UserPermissions'), (N'RefreshTokens'), (N'UserSessions'), (N'PendingSignIns'), (N'PasswordResetTokens');

-- Foreign keys of every table touched are switched off while data is deleted, and checked again at the end.
CREATE TABLE #Touched (Sch sysname NOT NULL, Tbl sysname NOT NULL);
INSERT INTO #Touched (Sch, Tbl) SELECT Sch, Tbl FROM #Wipe;
INSERT INTO #Touched (Sch, Tbl)
SELECT s.name, t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = N'identity' AND (t.name = N'Users' OR t.name IN (SELECT Tbl FROM #People));

CREATE TABLE #Fk (Disable_ nvarchar(max) NOT NULL, Enable_ nvarchar(max) NOT NULL);
INSERT INTO #Fk (Disable_, Enable_)
SELECT N'ALTER TABLE ' + QUOTENAME(ps.name) + N'.' + QUOTENAME(pt.name) + N' NOCHECK CONSTRAINT ' + QUOTENAME(fk.name),
       N'ALTER TABLE ' + QUOTENAME(ps.name) + N'.' + QUOTENAME(pt.name) + N' WITH CHECK CHECK CONSTRAINT ' + QUOTENAME(fk.name)
FROM sys.foreign_keys fk
JOIN sys.tables pt ON pt.object_id = fk.parent_object_id JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
WHERE fk.is_disabled = 0
  AND (EXISTS (SELECT 1 FROM #Touched x WHERE x.Sch = ps.name AND x.Tbl = pt.name)
    OR EXISTS (SELECT 1 FROM #Touched x WHERE x.Sch = rs.name AND x.Tbl = rt.name));

DECLARE @sql nvarchar(max), @sch sysname, @tbl sysname;
DECLARE fkc CURSOR LOCAL FAST_FORWARD FOR SELECT Disable_ FROM #Fk;
OPEN fkc; FETCH NEXT FROM fkc INTO @sql;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC (@sql); FETCH NEXT FROM fkc INTO @sql; END;
CLOSE fkc; DEALLOCATE fkc;

DECLARE wc CURSOR LOCAL FAST_FORWARD FOR SELECT Sch, Tbl FROM #Wipe;
OPEN wc; FETCH NEXT FROM wc INTO @sch, @tbl;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'DELETE FROM ' + QUOTENAME(@sch) + N'.' + QUOTENAME(@tbl);
    EXEC (@sql);
    PRINT N'  wiped ' + @sch + N'.' + @tbl + N': ' + CAST(@@ROWCOUNT AS nvarchar(20));
    FETCH NEXT FROM wc INTO @sch, @tbl;
END;
CLOSE wc; DEALLOCATE wc;

DECLARE pc CURSOR LOCAL FAST_FORWARD FOR SELECT Tbl FROM #People;
OPEN pc; FETCH NEXT FROM pc INTO @tbl;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'[identity].' + QUOTENAME(@tbl), N'U') IS NOT NULL AND COL_LENGTH(N'[identity].' + QUOTENAME(@tbl), N'UserId') IS NOT NULL
    BEGIN
        SET @sql = N'DELETE FROM [identity].' + QUOTENAME(@tbl) + N' WHERE UserId <> @a';
        EXEC sp_executesql @sql, N'@a uniqueidentifier', @a = @Admin;
        PRINT N'  wiped identity.' + @tbl + N' (other people): ' + CAST(@@ROWCOUNT AS nvarchar(20));
    END;
    FETCH NEXT FROM pc INTO @tbl;
END;
CLOSE pc; DEALLOCATE pc;

DELETE FROM [identity].[Users] WHERE Id <> @Admin AND IsSystem = 0;
PRINT N'  wiped identity.Users (everyone but the administrator): ' + CAST(@@ROWCOUNT AS nvarchar(20));

------------------------------------------------------------------------------------------------ lookup data
CREATE TABLE #Num (n int NOT NULL PRIMARY KEY);
INSERT INTO #Num (n) SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_columns a CROSS JOIN sys.all_columns b;

CREATE TABLE #Team (TeamIdx int NOT NULL PRIMARY KEY, Code nvarchar(20) NOT NULL, TeamName nvarchar(120) NOT NULL, UnitName nvarchar(120) NOT NULL,
                    GroupId uniqueidentifier NOT NULL DEFAULT NEWID(), UnitId uniqueidentifier NOT NULL DEFAULT NEWID());
INSERT INTO #Team (TeamIdx, Code, TeamName, UnitName) VALUES
    (1, N'tech', N'تیم فنی', N'واحد فنی'),
    (2, N'product', N'تیم محصول', N'واحد محصول'),
    (3, N'support', N'تیم پشتیبانی', N'واحد پشتیبانی'),
    (4, N'finance', N'تیم مالی و اداری', N'واحد مالی و اداری'),
    (5, N'marketing', N'تیم بازاریابی و فروش', N'واحد بازاریابی و فروش');

CREATE TABLE #First (Idx int NOT NULL PRIMARY KEY, Txt nvarchar(80) NOT NULL);
INSERT INTO #First (Idx, Txt) VALUES
    (1, N'آرش'),
    (2, N'آرمین'),
    (3, N'آیدا'),
    (4, N'ابوالفضل'),
    (5, N'احسان'),
    (6, N'ارسلان'),
    (7, N'اسماعیل'),
    (8, N'افسانه'),
    (9, N'امید'),
    (10, N'ایمان'),
    (11, N'بابک'),
    (12, N'بنفشه'),
    (13, N'پارسا'),
    (14, N'پریسا'),
    (15, N'تینا'),
    (16, N'جواد'),
    (17, N'حامد'),
    (18, N'حمید'),
    (19, N'خدیجه'),
    (20, N'داریوش'),
    (21, N'دانیال'),
    (22, N'رحیم'),
    (23, N'روزبه'),
    (24, N'ساناز'),
    (25, N'سجاد'),
    (26, N'سعید'),
    (27, N'سمیرا'),
    (28, N'سهیل'),
    (29, N'شادی'),
    (30, N'شهاب'),
    (31, N'صبا'),
    (32, N'طاها'),
    (33, N'عرفان'),
    (34, N'غزل'),
    (35, N'کاوه'),
    (36, N'کیان'),
    (37, N'ماهان'),
    (38, N'مریم'),
    (39, N'ندا'),
    (40, N'هدی'),
    (41, N'یاسمن'),
    (42, N'یاسر');
CREATE TABLE #Last (Idx int NOT NULL PRIMARY KEY, Txt nvarchar(80) NOT NULL);
INSERT INTO #Last (Idx, Txt) VALUES
    (1, N'احمدی'),
    (2, N'اصفهانی'),
    (3, N'امینی'),
    (4, N'انصاری'),
    (5, N'بهرامی'),
    (6, N'پناهی'),
    (7, N'تقوی'),
    (8, N'جلالی'),
    (9, N'حیدری'),
    (10, N'خسروی'),
    (11, N'دانشور'),
    (12, N'ذاکری'),
    (13, N'رستمی'),
    (14, N'زارعی'),
    (15, N'سعیدی'),
    (16, N'سلطانی'),
    (17, N'شفیعی'),
    (18, N'صالحی'),
    (19, N'طباطبایی'),
    (20, N'عباسی'),
    (21, N'فرهادی'),
    (22, N'قنبری'),
    (23, N'کریمخانی'),
    (24, N'گلشن'),
    (25, N'لطفی'),
    (26, N'مختاری'),
    (27, N'مهدوی'),
    (28, N'میرزایی'),
    (29, N'نادری'),
    (30, N'نجفی'),
    (31, N'هاشمی'),
    (32, N'همتی'),
    (33, N'وکیلی'),
    (34, N'یوسفی'),
    (35, N'آذری'),
    (36, N'بیگی'),
    (37, N'پورمحمدی'),
    (38, N'توکلی'),
    (39, N'سبحانی'),
    (40, N'فتحی');
CREATE TABLE #Job (TeamIdx int NOT NULL, Idx int NOT NULL, Txt nvarchar(120) NOT NULL, PRIMARY KEY (TeamIdx, Idx));
INSERT INTO #Job (TeamIdx, Idx, Txt) VALUES
    (1, 1, N'توسعه‌دهندهٔ بک‌اند'),
    (1, 2, N'توسعه‌دهندهٔ فرانت‌اند'),
    (1, 3, N'کارشناس تست نرم‌افزار'),
    (1, 4, N'مهندس زیرساخت'),
    (1, 5, N'کارشناس امنیت اطلاعات'),
    (1, 6, N'توسعه‌دهندهٔ ارشد'),
    (1, 7, N'مهندس DevOps'),
    (2, 1, N'طراح تجربهٔ کاربری'),
    (2, 2, N'تحلیل‌گر کسب‌وکار'),
    (2, 3, N'کارشناس محصول'),
    (2, 4, N'طراح رابط کاربری'),
    (2, 5, N'پژوهشگر کاربر'),
    (3, 1, N'کارشناس پشتیبانی'),
    (3, 2, N'کارشناس آموزش مشتریان'),
    (3, 3, N'کارشناس پایگاه دانش'),
    (3, 4, N'کارشناس پشتیبانی ارشد'),
    (4, 1, N'کارشناس حسابداری'),
    (4, 2, N'کارشناس منابع انسانی'),
    (4, 3, N'کارشناس خرید'),
    (4, 4, N'کارشناس بودجه'),
    (4, 5, N'کارشناس اداری'),
    (5, 1, N'کارشناس فروش'),
    (5, 2, N'کارشناس محتوا'),
    (5, 3, N'کارشناس ارتباط با مشتری'),
    (5, 4, N'کارشناس بازاریابی دیجیتال'),
    (5, 5, N'کارشناس برگزاری نمایشگاه');
CREATE TABLE #Verb (TeamIdx int NOT NULL, Idx int NOT NULL, Txt nvarchar(80) NOT NULL, PRIMARY KEY (TeamIdx, Idx));
INSERT INTO #Verb (TeamIdx, Idx, Txt) VALUES
    (1, 1, N'بررسی'),
    (1, 2, N'رفع'),
    (1, 3, N'پیاده‌سازی'),
    (1, 4, N'تست'),
    (1, 5, N'مستندسازی'),
    (1, 6, N'بهینه‌سازی'),
    (1, 7, N'بازبینی'),
    (2, 1, N'تحلیل'),
    (2, 2, N'طراحی'),
    (2, 3, N'بازنگری'),
    (2, 4, N'اولویت‌بندی'),
    (2, 5, N'مصاحبه دربارهٔ'),
    (3, 1, N'پیگیری'),
    (3, 2, N'پاسخ به'),
    (3, 3, N'آموزش'),
    (3, 4, N'بررسی'),
    (4, 1, N'صدور'),
    (4, 2, N'پیگیری'),
    (4, 3, N'تنظیم'),
    (4, 4, N'بررسی'),
    (4, 5, N'تسویهٔ'),
    (5, 1, N'پیگیری'),
    (5, 2, N'تهیهٔ'),
    (5, 3, N'ارسال'),
    (5, 4, N'تحلیل'),
    (5, 5, N'هماهنگی');
CREATE TABLE #Obj (TeamIdx int NOT NULL, Idx int NOT NULL, Txt nvarchar(120) NOT NULL, PRIMARY KEY (TeamIdx, Idx));
INSERT INTO #Obj (TeamIdx, Idx, Txt) VALUES
    (1, 1, N'خطای گزارش‌گیری'),
    (1, 2, N'کندی صفحهٔ بورد'),
    (1, 3, N'سرویس اعلان‌ها'),
    (1, 4, N'دسترسی‌های کاربران'),
    (1, 5, N'همگام‌سازی LDAP'),
    (1, 6, N'پشتیبان‌گیری'),
    (1, 7, N'API فعالیت‌ها'),
    (1, 8, N'ورود با پیامک'),
    (1, 9, N'خروجی Excel'),
    (2, 1, N'درخواست‌های امکانات'),
    (2, 2, N'جریان ثبت فعالیت'),
    (2, 3, N'داشبورد مدیران'),
    (2, 4, N'گزارش‌های دوره‌ای'),
    (2, 5, N'تجربهٔ موبایل'),
    (2, 6, N'سند نیازمندی'),
    (3, 1, N'تیکت‌های باز'),
    (3, 2, N'مشکل ورود کاربران'),
    (3, 3, N'درخواست نصب نسخهٔ جدید'),
    (3, 4, N'راهنمای کاربری'),
    (3, 5, N'نظرسنجی رضایت'),
    (4, 1, N'فاکتور قرارداد'),
    (4, 2, N'پرداخت معوق'),
    (4, 3, N'برآورد بودجه'),
    (4, 4, N'لیست بیمه'),
    (4, 5, N'صورت‌حساب'),
    (5, 1, N'پیش‌فاکتور'),
    (5, 2, N'پیشنهاد تمدید'),
    (5, 3, N'محتوای کمپین'),
    (5, 4, N'گزارش فروش'),
    (5, 5, N'جلسهٔ دمو');
CREATE TABLE #Client (Idx int NOT NULL PRIMARY KEY, Txt nvarchar(120) NOT NULL);
INSERT INTO #Client (Idx, Txt) VALUES
    (1, N'شرکت آریا'),
    (2, N'بانک ملت'),
    (3, N'هلدینگ پارسیان'),
    (4, N'گروه صنعتی سپاهان'),
    (5, N'بیمهٔ آسیا'),
    (6, N'شرکت نفت پاسارگاد'),
    (7, N'دانشگاه شریف'),
    (8, N'فروشگاه‌های رفاه');
CREATE TABLE #GDesc (Idx int NOT NULL PRIMARY KEY, Txt nvarchar(400) NOT NULL);
INSERT INTO #GDesc (Idx, Txt) VALUES
    (1, N'جزئیات در جلسهٔ هفتگی توضیح داده شد؛ در صورت نیاز با مدیر واحد هماهنگ شود.'),
    (2, N'اولویت با درخواست‌هایی است که مشتری پیگیر آن است.'),
    (3, N'نتیجه را در پوشهٔ مشترک بگذارید و در گفتگوی فعالیت اطلاع دهید.'),
    (4, N'پیش از شروع، وابستگی‌ها و زمان لازم برآورد شود.');
CREATE TABLE #Tag (TeamIdx int NOT NULL, Idx int NOT NULL, Txt nvarchar(100) NOT NULL, TagId uniqueidentifier NOT NULL DEFAULT NEWID(), PRIMARY KEY (TeamIdx, Idx));
INSERT INTO #Tag (TeamIdx, Idx, Txt) VALUES
    (1, 1, N'بک‌اند'),
    (1, 2, N'فرانت‌اند'),
    (1, 3, N'زیرساخت'),
    (1, 4, N'امنیت'),
    (1, 5, N'باگ'),
    (1, 6, N'بهینه‌سازی'),
    (2, 1, N'طراحی'),
    (2, 2, N'نیازمندی'),
    (2, 3, N'تحقیق کاربر'),
    (2, 4, N'نقشهٔ راه'),
    (3, 1, N'تیکت'),
    (3, 2, N'آموزش'),
    (3, 3, N'مشتری'),
    (4, 1, N'مالی'),
    (4, 2, N'منابع انسانی'),
    (4, 3, N'خرید'),
    (4, 4, N'قرارداد'),
    (5, 1, N'فروش'),
    (5, 2, N'محتوا'),
    (5, 3, N'کمپین'),
    (5, 4, N'نمایشگاه');
CREATE TABLE #Proj (Idx int NOT NULL PRIMARY KEY, TeamIdx int NOT NULL, Title nvarchar(200) NOT NULL, TaskId uniqueidentifier NOT NULL DEFAULT NEWID());
INSERT INTO #Proj (Idx, TeamIdx, Title) VALUES
    (1, 1, N'پروژهٔ راه‌اندازی نسخهٔ ۳ سامانه'),
    (2, 2, N'پروژهٔ بازطراحی تجربهٔ کاربری'),
    (3, 5, N'پروژهٔ حضور در نمایشگاه الکامپ'),
    (4, 4, N'پروژهٔ استقرار نرم‌افزار حقوق و دستمزد'),
    (5, 3, N'پروژهٔ راه‌اندازی مرکز تماس'),
    (6, 1, N'پروژهٔ مهاجرت زیرساخت به ابر خصوصی'),
    (7, 2, N'پروژهٔ سامانهٔ نظرسنجی مشتریان'),
    (8, 5, N'پروژهٔ برند و وب‌سایت جدید'),
    (9, 4, N'پروژهٔ ممیزی داخلی سالانه'),
    (10, 3, N'پروژهٔ بازطراحی پایگاه دانش');
CREATE TABLE #Step (ProjIdx int NOT NULL, Ord int NOT NULL, Title nvarchar(200) NOT NULL, SubTaskId uniqueidentifier NOT NULL DEFAULT NEWID(), PRIMARY KEY (ProjIdx, Ord));
INSERT INTO #Step (ProjIdx, Ord, Title) VALUES
    (1, 1, N'تحلیل نیازمندی‌ها'),
    (1, 2, N'طراحی معماری'),
    (1, 3, N'پیاده‌سازی ماژول‌های اصلی'),
    (1, 4, N'تست یکپارچه'),
    (1, 5, N'استقرار روی سرور اصلی'),
    (2, 1, N'مصاحبه با کاربران'),
    (2, 2, N'طراحی وایرفریم'),
    (2, 3, N'طراحی نهایی'),
    (2, 4, N'آزمون کاربردپذیری'),
    (3, 1, N'رزرو غرفه'),
    (3, 2, N'طراحی غرفه'),
    (3, 3, N'تهیهٔ کاتالوگ و هدایا'),
    (3, 4, N'هماهنگی تیم حاضر'),
    (3, 5, N'پیگیری سرنخ‌ها'),
    (4, 1, N'انتخاب نرم‌افزار'),
    (4, 2, N'انتقال اطلاعات پرسنل'),
    (4, 3, N'اجرای آزمایشی'),
    (4, 4, N'اجرای نهایی'),
    (5, 1, N'خرید سامانهٔ تلفنی'),
    (5, 2, N'آموزش کارشناسان'),
    (5, 3, N'تدوین اسکریپت پاسخگویی'),
    (6, 1, N'برآورد منابع'),
    (6, 2, N'راه‌اندازی خوشهٔ آزمایشی'),
    (6, 3, N'انتقال سرویس‌های غیرحیاتی'),
    (6, 4, N'انتقال پایگاه داده'),
    (6, 5, N'قطع سرور قدیمی'),
    (7, 1, N'تعریف پرسشنامه'),
    (7, 2, N'طراحی صفحهٔ نظرسنجی'),
    (7, 3, N'اتصال به تیکت‌ها'),
    (7, 4, N'گزارش نتایج'),
    (8, 1, N'تحقیق بازار'),
    (8, 2, N'هویت بصری'),
    (8, 3, N'طراحی وب‌سایت'),
    (8, 4, N'تولید محتوا'),
    (8, 5, N'راه‌اندازی و سئو'),
    (9, 1, N'برنامهٔ ممیزی'),
    (9, 2, N'جمع‌آوری مستندات'),
    (9, 3, N'بررسی ریسک‌ها'),
    (9, 4, N'تهیهٔ گزارش نهایی'),
    (10, 1, N'بررسی مقاله‌های فعلی'),
    (10, 2, N'ساختار جدید دسته‌ها'),
    (10, 3, N'بازنویسی ۴۰ مقاله'),
    (10, 4, N'راه‌اندازی جستجو');
CREATE TABLE #Comment (Idx int NOT NULL PRIMARY KEY, Txt nvarchar(400) NOT NULL);
INSERT INTO #Comment (Idx, Txt) VALUES
    (1, N'بررسی کردم، تا فردا نتیجه را اعلام می‌کنم.'),
    (2, N'فایل نهایی را پیوست کنید لطفاً.'),
    (3, N'این مورد به هماهنگی با واحد مالی نیاز دارد.'),
    (4, N'انجام شد؛ لطفاً بازبینی کنید.'),
    (5, N'آیا موعد قابل تمدید است؟ یک روز دیگر لازم دارم.'),
    (6, N'با مشتری تماس گرفتم، منتظر پاسخشان هستیم.'),
    (7, N'نسخهٔ اول آماده است، بازخوردتان را بنویسید.'),
    (8, N'این را در جلسهٔ شنبه مطرح کنیم.'),
    (9, N'مشکل از تنظیمات سرور بود، اصلاح شد.'),
    (10, N'لطفاً اولویت این کار را بالا ببرید، مشتری منتظر است.'),
    (11, N'پیش‌نویس را در پوشهٔ مشترک گذاشتم.'),
    (12, N'موافقم، با همین روش جلو برویم.');
CREATE TABLE #Note (Idx int NOT NULL PRIMARY KEY, Title nvarchar(200) NOT NULL, Content nvarchar(max) NOT NULL, Color nvarchar(30) NOT NULL, Pinned bit NOT NULL);
INSERT INTO #Note (Idx, Title, Content, Color, Pinned) VALUES
    (1, N'کارهای این هفته', N'- تماس با مشتری آریا
- بازبینی گزارش فروش
- آماده‌سازی جلسهٔ شنبه', N'amber', 1),
    (2, N'ایده برای بهبود فرآیند', N'جلسه‌های روزانه کوتاه‌تر شود (حداکثر ۱۰ دقیقه) و هر کس فقط مانع‌ها را بگوید.', N'indigo', 0),
    (3, N'شماره‌های مهم', N'پشتیبانی سرور: ۰۲۱-۸۸۷۷۶۶۵۵
حسابداری ساختمان: ۰۲۱-۸۸۲۲۳۳۴۴', N'slate', 0),
    (4, N'یادآوری', N'تمدید بیمهٔ تکمیلی تا پانزدهم ماه', N'rose', 1),
    (5, N'کتاب‌هایی که باید بخوانم', N'مدیریت زمان برای مدیران
طراحی محصولات دوست‌داشتنی', N'emerald', 0);
CREATE TABLE #Curated (TeamIdx int NOT NULL, Title nvarchar(200) NOT NULL, Descr nvarchar(4000) NULL);
INSERT INTO #Curated (TeamIdx, Title, Descr) VALUES
    (1, N'رفع خطای ورود با شمارهٔ همراه در اندروید', N'کاربران گوشی‌های اندروید ۱۲ بعد از وارد کردن کد تأیید به صفحهٔ ورود برمی‌گردند. لاگ سرور و نسخهٔ مرورگر بررسی شود.'),
    (1, N'مهاجرت پایگاه داده به SQL Server 2022', N'نسخهٔ پشتیبان کامل، اجرای آزمایشی روی سرور تست و برنامهٔ زمان‌بندی قطعی کوتاه برای سرور اصلی.'),
    (1, N'پیاده‌سازی صفحه‌بندی در گزارش فعالیت‌ها', N'گزارش با بیش از ۵۰۰۰ ردیف کند است؛ صفحه‌بندی سمت سرور و ایندکس روی تاریخ اضافه شود.'),
    (1, N'به‌روزرسانی گواهی SSL دامنهٔ اصلی', N'گواهی فعلی تا آخر ماه معتبر است. گواهی جدید تهیه و روی IIS و load balancer نصب شود.'),
    (1, N'بهینه‌سازی زمان بارگذاری بورد', N'هدف: بارگذاری کمتر از ۲ ثانیه برای ۳۰۰ فعالیت.'),
    (1, N'راه‌اندازی پشتیبان‌گیری شبانه خودکار', N'اسکریپت backup.ps1 در Task Scheduler ساعت ۲ بامداد و نگهداری ۱۴ نسخه.'),
    (1, N'افزودن احراز هویت دومرحله‌ای برای مدیران', N'برای همهٔ حساب‌های مدیر، ورود با کد پیامکی اجباری شود.'),
    (1, N'بازنویسی سرویس ارسال پیامک', N'جایگزینی کتابخانهٔ قدیمی و افزودن تلاش مجدد در صورت خطای سرویس‌دهنده.'),
    (1, N'بررسی هشدارهای امنیتی وابستگی‌ها', N'خروجی npm audit و dotnet list package --vulnerable بررسی و موارد بحرانی رفع شوند.'),
    (1, N'طراحی API گزارش ساعت کار تیم', N'خروجی هفتگی بر اساس تیم و پروژه، با امکان دریافت Excel.'),
    (1, N'رفع کندی جستجو در گفتگوها', NULL),
    (1, N'نوشتن تست خودکار برای فرم ثبت فعالیت', N'پوشش حالت‌های موعد تعطیل، مسئول چندنفره و پیوست.'),
    (1, N'پایش مصرف حافظهٔ سرور برنامه', N'از هفتهٔ گذشته مصرف حافظه رو به افزایش است؛ احتمال نشت حافظه بررسی شود.'),
    (1, N'ارتقای Node.js سرور فرانت به نسخهٔ ۲۲', NULL),
    (1, N'مستندسازی API برای تیم یکپارچه‌سازی', N'Swagger کامل با نمونهٔ درخواست و پاسخ برای سرویس‌های فعالیت و تقویم.'),
    (1, N'رفع گزارش خطای شرکت آریا در خروجی Excel', N'خروجی گزارش برای بیش از ۱۰۰۰ ردیف ناقص است.'),
    (1, N'رفع گزارش خطای بانک ملت در خروجی Excel', N'خروجی گزارش برای بیش از ۱۰۰۰ ردیف ناقص است.'),
    (1, N'رفع گزارش خطای هلدینگ پارسیان در خروجی Excel', N'خروجی گزارش برای بیش از ۱۰۰۰ ردیف ناقص است.'),
    (1, N'رفع گزارش خطای گروه صنعتی سپاهان در خروجی Excel', N'خروجی گزارش برای بیش از ۱۰۰۰ ردیف ناقص است.'),
    (1, N'رفع گزارش خطای بیمهٔ آسیا در خروجی Excel', N'خروجی گزارش برای بیش از ۱۰۰۰ ردیف ناقص است.'),
    (1, N'رفع گزارش خطای شرکت نفت پاسارگاد در خروجی Excel', N'خروجی گزارش برای بیش از ۱۰۰۰ ردیف ناقص است.'),
    (1, N'تنظیم دسترسی کاربران جدید شرکت آریا', NULL),
    (1, N'تنظیم دسترسی کاربران جدید بانک ملت', NULL),
    (1, N'تنظیم دسترسی کاربران جدید هلدینگ پارسیان', NULL),
    (1, N'تنظیم دسترسی کاربران جدید گروه صنعتی سپاهان', NULL),
    (1, N'تنظیم دسترسی کاربران جدید بیمهٔ آسیا', NULL),
    (1, N'تنظیم دسترسی کاربران جدید شرکت نفت پاسارگاد', NULL),
    (1, N'بررسی کندی سامانه در سرور شرکت آریا', N'زمان پاسخ صفحهٔ بورد در ساعات اوج بیش از ۵ ثانیه است.'),
    (1, N'بررسی کندی سامانه در سرور بانک ملت', N'زمان پاسخ صفحهٔ بورد در ساعات اوج بیش از ۵ ثانیه است.'),
    (1, N'بررسی کندی سامانه در سرور هلدینگ پارسیان', N'زمان پاسخ صفحهٔ بورد در ساعات اوج بیش از ۵ ثانیه است.'),
    (1, N'بررسی کندی سامانه در سرور گروه صنعتی سپاهان', N'زمان پاسخ صفحهٔ بورد در ساعات اوج بیش از ۵ ثانیه است.'),
    (1, N'بررسی کندی سامانه در سرور بیمهٔ آسیا', N'زمان پاسخ صفحهٔ بورد در ساعات اوج بیش از ۵ ثانیه است.'),
    (1, N'بررسی کندی سامانه در سرور شرکت نفت پاسارگاد', N'زمان پاسخ صفحهٔ بورد در ساعات اوج بیش از ۵ ثانیه است.'),
    (2, N'طراحی صفحهٔ جدید داشبورد مدیران', N'شاخص‌های کلیدی: فعالیت‌های عقب‌افتاده، بار کاری تیم‌ها و روند تکمیل هفتگی.'),
    (2, N'مصاحبه با ۵ مشتری دربارهٔ گزارش‌ها', N'هدف: فهمیدن اینکه مدیران کدام گزارش را هر هفته باز می‌کنند و چه چیزی کم دارند.'),
    (2, N'تدوین نقشهٔ راه سه‌ماههٔ محصول', N'اولویت‌بندی بر اساس درخواست مشتریان و ظرفیت تیم فنی.'),
    (2, N'نهایی‌کردن سند نیازمندی ماژول قراردادها', NULL),
    (2, N'بازطراحی فرم ثبت فعالیت', N'تعداد فیلدها کم شود و دسته‌بندی هوشمند پیشنهاد داده شود.'),
    (2, N'تحلیل بازخورد کاربران از نسخهٔ ۲.۴', N'دسته‌بندی ۱۲۰ بازخورد ثبت‌شده و استخراج ۱۰ مشکل پرتکرار.'),
    (2, N'طراحی حالت تیرهٔ برنامهٔ موبایل', NULL),
    (2, N'آماده‌سازی دموی محصول برای نمایشگاه', N'سناریوی ۱۰ دقیقه‌ای با دادهٔ نمونه و نسخهٔ آفلاین.'),
    (2, N'بررسی رقبا در حوزهٔ مدیریت پروژه', N'مقایسهٔ قیمت، امکانات و تجربهٔ کاربری سه رقیب اصلی.'),
    (2, N'تعریف شاخص‌های موفقیت نسخهٔ جدید', N'نرخ استفادهٔ هفتگی، زمان ثبت فعالیت و رضایت کاربران.'),
    (2, N'جمع‌بندی درخواست‌های امکانات شرکت آریا', N'درخواست‌های ثبت‌شده در سه ماه اخیر دسته‌بندی شود.'),
    (2, N'جمع‌بندی درخواست‌های امکانات بانک ملت', N'درخواست‌های ثبت‌شده در سه ماه اخیر دسته‌بندی شود.'),
    (2, N'جمع‌بندی درخواست‌های امکانات هلدینگ پارسیان', N'درخواست‌های ثبت‌شده در سه ماه اخیر دسته‌بندی شود.'),
    (2, N'جمع‌بندی درخواست‌های امکانات گروه صنعتی سپاهان', N'درخواست‌های ثبت‌شده در سه ماه اخیر دسته‌بندی شود.'),
    (3, N'پاسخ به تیکت‌های باز شرکت آریا', N'۷ تیکت باز دربارهٔ دسترسی کاربران و گزارش‌ها.'),
    (3, N'تهیهٔ ویدیوی آموزشی ثبت فعالیت', N'ویدیوی ۳ دقیقه‌ای برای کاربران جدید.'),
    (3, N'آموزش حضوری کاربران واحد مالی', N'جلسهٔ دو ساعته در دفتر مشتری؛ هماهنگی با آقای یزدانی.'),
    (3, N'به‌روزرسانی راهنمای کاربری نسخهٔ جدید', NULL),
    (3, N'پیگیری مشکل همگام‌سازی LDAP مشتری', N'کاربران غیرفعال در Active Directory هنوز وارد سامانه می‌شوند.'),
    (3, N'گزارش ماهانهٔ رضایت مشتریان', N'جمع‌بندی نظرسنجی بعد از بستن تیکت‌ها.'),
    (3, N'تماس با مشتریانی که بیش از ۳۰ روز وارد نشده‌اند', NULL),
    (3, N'تکمیل پایگاه دانش پرسش‌های پرتکرار', N'۲۰ پرسش پرتکرار با پاسخ کوتاه و تصویر.'),
    (3, N'نصب نسخهٔ جدید روی سرور شرکت پارسیان', N'هماهنگی زمان قطعی با واحد فناوری مشتری.'),
    (3, N'پاسخ به تیکت‌های هفتهٔ شرکت آریا', NULL),
    (3, N'پاسخ به تیکت‌های هفتهٔ بانک ملت', NULL),
    (3, N'پاسخ به تیکت‌های هفتهٔ هلدینگ پارسیان', NULL),
    (3, N'پاسخ به تیکت‌های هفتهٔ گروه صنعتی سپاهان', NULL),
    (3, N'پاسخ به تیکت‌های هفتهٔ بیمهٔ آسیا', NULL),
    (3, N'پاسخ به تیکت‌های هفتهٔ شرکت نفت پاسارگاد', NULL),
    (3, N'آموزش آنلاین کاربران شرکت آریا', N'وبینار یک ساعته برای مدیران واحدها.'),
    (3, N'آموزش آنلاین کاربران بانک ملت', N'وبینار یک ساعته برای مدیران واحدها.'),
    (3, N'آموزش آنلاین کاربران هلدینگ پارسیان', N'وبینار یک ساعته برای مدیران واحدها.'),
    (3, N'آموزش آنلاین کاربران گروه صنعتی سپاهان', N'وبینار یک ساعته برای مدیران واحدها.'),
    (3, N'آموزش آنلاین کاربران بیمهٔ آسیا', N'وبینار یک ساعته برای مدیران واحدها.'),
    (3, N'آموزش آنلاین کاربران شرکت نفت پاسارگاد', N'وبینار یک ساعته برای مدیران واحدها.'),
    (3, N'نصب به‌روزرسانی روی سرور شرکت آریا', NULL),
    (3, N'نصب به‌روزرسانی روی سرور بانک ملت', NULL),
    (3, N'نصب به‌روزرسانی روی سرور هلدینگ پارسیان', NULL),
    (3, N'نصب به‌روزرسانی روی سرور گروه صنعتی سپاهان', NULL),
    (3, N'نصب به‌روزرسانی روی سرور بیمهٔ آسیا', NULL),
    (3, N'نصب به‌روزرسانی روی سرور شرکت نفت پاسارگاد', NULL),
    (4, N'تهیهٔ گزارش مالی سه‌ماههٔ پاییز', N'صورت سود و زیان و جریان نقدی برای جلسهٔ هیئت‌مدیره.'),
    (4, N'تمدید قرارداد اجارهٔ دفتر مرکزی', N'مذاکره برای افزایش حداکثر ۲۰ درصدی و تمدید دوساله.'),
    (4, N'پرداخت حقوق و مزایای آذرماه', NULL),
    (4, N'جذب کارشناس پشتیبانی جدید', N'آگهی، غربالگری رزومه‌ها و هماهنگی مصاحبهٔ فنی.'),
    (4, N'خرید ۴ دستگاه لپ‌تاپ برای تیم فنی', N'استعلام از سه تأمین‌کننده و تأیید مدیر فنی.'),
    (4, N'تسویهٔ صورت‌حساب‌های معوق مشتریان', N'پیگیری ۹ فاکتور با بیش از ۶۰ روز تأخیر.'),
    (4, N'برگزاری دورهٔ آموزشی ایمنی کار', NULL),
    (4, N'تنظیم بودجهٔ سال آینده', N'دریافت برآورد هزینهٔ هر واحد تا پایان ماه.'),
    (4, N'ارسال لیست بیمهٔ تأمین اجتماعی', NULL),
    (4, N'صدور فاکتور قرارداد شرکت آریا', NULL),
    (4, N'صدور فاکتور قرارداد بانک ملت', NULL),
    (4, N'صدور فاکتور قرارداد هلدینگ پارسیان', NULL),
    (4, N'صدور فاکتور قرارداد گروه صنعتی سپاهان', NULL),
    (4, N'صدور فاکتور قرارداد بیمهٔ آسیا', NULL),
    (4, N'صدور فاکتور قرارداد شرکت نفت پاسارگاد', NULL),
    (4, N'پیگیری پرداخت معوق شرکت آریا', N'دو فاکتور با بیش از ۴۵ روز تأخیر.'),
    (4, N'پیگیری پرداخت معوق بانک ملت', N'دو فاکتور با بیش از ۴۵ روز تأخیر.'),
    (4, N'پیگیری پرداخت معوق هلدینگ پارسیان', N'دو فاکتور با بیش از ۴۵ روز تأخیر.'),
    (4, N'پیگیری پرداخت معوق گروه صنعتی سپاهان', N'دو فاکتور با بیش از ۴۵ روز تأخیر.'),
    (4, N'پیگیری پرداخت معوق بیمهٔ آسیا', N'دو فاکتور با بیش از ۴۵ روز تأخیر.'),
    (4, N'پیگیری پرداخت معوق شرکت نفت پاسارگاد', N'دو فاکتور با بیش از ۴۵ روز تأخیر.'),
    (5, N'کمپین معرفی نسخهٔ جدید در لینکدین', N'سه پست، یک ویدیوی کوتاه و یک وبینار معرفی.'),
    (5, N'پیگیری پیش‌فاکتور شرکت آریا', N'پیش‌فاکتور ۱۲۰ کاربره؛ پاسخ تا پنجشنبه.'),
    (5, N'تهیهٔ کاتالوگ محصول برای نمایشگاه', NULL),
    (5, N'جلسهٔ دمو با بانک ملت', N'نمایش ماژول تأیید و گزارش‌ها برای واحد فناوری بانک.'),
    (5, N'نوشتن مقالهٔ مدیریت زمان در تیم‌های توزیع‌شده', NULL),
    (5, N'به‌روزرسانی صفحهٔ قیمت‌گذاری سایت', N'افزودن پلن سازمانی و پرسش‌های پرتکرار.'),
    (5, N'تحلیل نتایج کمپین ایمیلی آبان', N'نرخ باز شدن، کلیک و ثبت‌نام آزمایشی.'),
    (5, N'تماس با ۲۰ سرنخ فروش نمایشگاه', NULL),
    (5, N'تهیهٔ گزارش فروش ماهانه برای مدیرعامل', N'مقایسه با ماه قبل و پیش‌بینی ماه آینده.'),
    (5, N'پیگیری پیش‌فاکتور بانک ملت', N'پاسخ مشتری تا پایان هفته لازم است.'),
    (5, N'پیگیری پیش‌فاکتور هلدینگ پارسیان', N'پاسخ مشتری تا پایان هفته لازم است.'),
    (5, N'پیگیری پیش‌فاکتور گروه صنعتی سپاهان', N'پاسخ مشتری تا پایان هفته لازم است.'),
    (5, N'پیگیری پیش‌فاکتور بیمهٔ آسیا', N'پاسخ مشتری تا پایان هفته لازم است.'),
    (5, N'پیگیری پیش‌فاکتور شرکت نفت پاسارگاد', N'پاسخ مشتری تا پایان هفته لازم است.'),
    (5, N'ارسال پیشنهاد تمدید قرارداد به شرکت آریا', NULL),
    (5, N'ارسال پیشنهاد تمدید قرارداد به بانک ملت', NULL),
    (5, N'ارسال پیشنهاد تمدید قرارداد به هلدینگ پارسیان', NULL),
    (5, N'ارسال پیشنهاد تمدید قرارداد به گروه صنعتی سپاهان', NULL),
    (5, N'ارسال پیشنهاد تمدید قرارداد به بیمهٔ آسیا', NULL),
    (5, N'ارسال پیشنهاد تمدید قرارداد به شرکت نفت پاسارگاد', NULL);
CREATE TABLE #PermName (RoleKey nvarchar(20) NOT NULL, Name nvarchar(120) NOT NULL);
INSERT INTO #PermName (RoleKey, Name) VALUES
    (N'staff', N'Tasks.View'),
    (N'staff', N'Tasks.Create'),
    (N'staff', N'Tasks.Edit'),
    (N'staff', N'Tasks.Comment'),
    (N'staff', N'Tasks.UploadFiles'),
    (N'staff', N'Tasks.Assign'),
    (N'staff', N'Notes.Manage'),
    (N'staff', N'Meetings.View'),
    (N'staff', N'Meetings.Create'),
    (N'staff', N'Workflow.View'),
    (N'staff', N'groups.view_own'),
    (N'staff', N'work_calendars.view'),
    (N'manager', N'Tasks.View'),
    (N'manager', N'Tasks.Create'),
    (N'manager', N'Tasks.Edit'),
    (N'manager', N'Tasks.Comment'),
    (N'manager', N'Tasks.UploadFiles'),
    (N'manager', N'Tasks.Assign'),
    (N'manager', N'Notes.Manage'),
    (N'manager', N'Meetings.View'),
    (N'manager', N'Meetings.Create'),
    (N'manager', N'Workflow.View'),
    (N'manager', N'groups.view_own'),
    (N'manager', N'work_calendars.view'),
    (N'manager', N'Tasks.Delete'),
    (N'manager', N'Tasks.ManageTags'),
    (N'manager', N'Tasks.ManageRecurring'),
    (N'manager', N'Workflow.Approve'),
    (N'manager', N'Workflow.Reject'),
    (N'manager', N'Meetings.Edit'),
    (N'manager', N'Meetings.Delete'),
    (N'manager', N'Meetings.InviteHigher'),
    (N'manager', N'users.view'),
    (N'manager', N'groups.manage_own');

DECLARE @nF int = (SELECT COUNT(*) FROM #First), @nL int = (SELECT COUNT(*) FROM #Last), @nC int = (SELECT COUNT(*) FROM #Client), @nCm int = (SELECT COUNT(*) FROM #Comment);

------------------------------------------------------------------------------------------------ people
CREATE TABLE #Person (G int NOT NULL PRIMARY KEY, TeamIdx int NOT NULL, IsManager bit NOT NULL, First nvarchar(80) NOT NULL, Last nvarchar(80) NOT NULL, Title nvarchar(120) NOT NULL,
                      UserId uniqueidentifier NOT NULL DEFAULT NEWID(), Username nvarchar(20) NULL, Phone nvarchar(20) NULL);
INSERT INTO #Person (G, First, Last, TeamIdx, IsManager, Title)
SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), b.First, b.Last, b.TeamIdx, b.IsManager, b.Title
FROM (VALUES
    (N'رضا', N'کریمی', 1, 1, N'مدیر فنی'),
    (N'سارا', N'رضایی', 2, 1, N'مدیر محصول'),
    (N'علی', N'مرادی', 1, 0, N'توسعه‌دهندهٔ ارشد بک‌اند'),
    (N'نگار', N'سلیمانی', 2, 0, N'طراح تجربهٔ کاربری'),
    (N'محمد', N'حسینی', 1, 0, N'توسعه‌دهندهٔ فرانت‌اند'),
    (N'فاطمه', N'جعفری', 1, 0, N'کارشناس تست نرم‌افزار'),
    (N'حسین', N'نوری', 1, 0, N'مهندس زیرساخت'),
    (N'زهرا', N'محمدی', 3, 1, N'سرپرست پشتیبانی'),
    (N'امیر', N'صادقی', 3, 0, N'کارشناس پشتیبانی'),
    (N'مهسا', N'کاظمی', 3, 0, N'کارشناس پشتیبانی'),
    (N'بهروز', N'یزدانی', 4, 1, N'مدیر مالی و اداری'),
    (N'لیلا', N'شریفی', 4, 0, N'کارشناس حسابداری'),
    (N'کامران', N'افشار', 4, 0, N'کارشناس منابع انسانی'),
    (N'شیرین', N'طاهری', 5, 1, N'مدیر بازاریابی و فروش'),
    (N'پویا', N'رحمانی', 5, 0, N'کارشناس فروش'),
    (N'الناز', N'قاسمی', 5, 0, N'کارشناس محتوا و شبکه‌های اجتماعی'),
    (N'مهدی', N'اکبری', 2, 0, N'تحلیل‌گر کسب‌وکار'),
    (N'نرگس', N'باقری', 5, 0, N'کارشناس ارتباط با مشتری')
) b (First, Last, TeamIdx, IsManager, Title);

INSERT INTO #Person (G, First, Last, TeamIdx, IsManager, Title)
SELECT 1000 + x.g, f.Txt, l.Txt, t.TeamIdx, 0, j.Txt
FROM #Team t
JOIN #Num n ON n.n <= @StaffPerTeam
CROSS APPLY (SELECT g = (t.TeamIdx - 1) * @StaffPerTeam + n.n) x
JOIN #First f ON f.Idx = (x.g * 5) % @nF + 1
JOIN #Last l ON l.Idx = (x.g / @nF + ((x.g * 5) % @nF) * 3) % @nL + 1
JOIN (SELECT TeamIdx, Idx, Txt, cnt = COUNT(*) OVER (PARTITION BY TeamIdx) FROM #Job) j ON j.TeamIdx = t.TeamIdx AND j.Idx = n.n % j.cnt + 1;

-- username = a valid national code (check digit as the system's own rule), mobile = 0912 + 7 digits; both fixed by the number
UPDATE p
SET Username = a.d + CAST(CASE WHEN b.s < 2 THEN b.s ELSE 11 - b.s END AS varchar(1)),
    Phone = N'0912' + CAST(1000000 + (CAST(p.G AS bigint) * 7331) % 8999999 AS varchar(7))
FROM #Person p
CROSS APPLY (SELECT d = CAST(100000000 + (CAST(p.G AS bigint) * 7919123) % 899999999 AS varchar(9))) a
CROSS APPLY (SELECT s = (CAST(SUBSTRING(a.d, 1, 1) AS int) * 10 + CAST(SUBSTRING(a.d, 2, 1) AS int) * 9 + CAST(SUBSTRING(a.d, 3, 1) AS int) * 8
                       + CAST(SUBSTRING(a.d, 4, 1) AS int) * 7 + CAST(SUBSTRING(a.d, 5, 1) AS int) * 6 + CAST(SUBSTRING(a.d, 6, 1) AS int) * 5
                       + CAST(SUBSTRING(a.d, 7, 1) AS int) * 4 + CAST(SUBSTRING(a.d, 8, 1) AS int) * 3 + CAST(SUBSTRING(a.d, 9, 1) AS int) * 2) % 11) b;

IF EXISTS (SELECT Username FROM #Person GROUP BY Username HAVING COUNT(*) > 1) OR EXISTS (SELECT Phone FROM #Person GROUP BY Phone HAVING COUNT(*) > 1)
    THROW 50001, N'Generated usernames or mobile numbers are not unique (too many people for the generator).', 1;
IF EXISTS (SELECT 1 FROM #Person p WHERE p.Username LIKE N'0000%' OR LEN(REPLACE(p.Username, LEFT(p.Username, 1), N'')) = 0)
    THROW 50002, N'A generated national code has all digits equal.', 1;
IF EXISTS (SELECT 1 FROM #Person p JOIN [identity].[Users] u ON u.Username = p.Username OR u.PhoneNumber = p.Phone)
    THROW 50003, N'A generated username or mobile number belongs to the administrator.', 1;

------------------------------------------------------------------------------------------------ roles
DECLARE @RoleStaff uniqueidentifier = (SELECT TOP (1) Id FROM [identity].[Roles] WHERE TenantId = @Tenant AND NormalizedName = N'کارمند');
IF @RoleStaff IS NULL
BEGIN
    SET @RoleStaff = NEWID();
    INSERT INTO [identity].[Roles] (Id, TenantId, Name, NormalizedName, Description, IsSystem, CreatedAtUtc, CreatedByUserId)
    VALUES (@RoleStaff, @Tenant, N'کارمند', N'کارمند', N'کار روزمره: فعالیت، یادداشت، جلسه و گفتگو', 0, @Now, @Admin);
END;
DECLARE @RoleManager uniqueidentifier = (SELECT TOP (1) Id FROM [identity].[Roles] WHERE TenantId = @Tenant AND NormalizedName = N'مدیر واحد');
IF @RoleManager IS NULL
BEGIN
    SET @RoleManager = NEWID();
    INSERT INTO [identity].[Roles] (Id, TenantId, Name, NormalizedName, Description, IsSystem, CreatedAtUtc, CreatedByUserId)
    VALUES (@RoleManager, @Tenant, N'مدیر واحد', N'مدیر واحد', N'به‌علاوهٔ تأیید و حذف فعالیت و ویرایش جلسه‌ها', 0, @Now, @Admin);
END;
DELETE FROM [identity].[RolePermissions] WHERE RoleId IN (@RoleStaff, @RoleManager);
INSERT INTO [identity].[RolePermissions] (RoleId, PermissionId)
SELECT CASE pn.RoleKey WHEN N'staff' THEN @RoleStaff ELSE @RoleManager END, pm.Id
FROM #PermName pn JOIN [identity].[Permissions] pm ON pm.Name = pn.Name;

------------------------------------------------------------------------------------------------ users, roles, teams
INSERT INTO [identity].[Users] (Id, TenantId, Username, FirstName, LastName, DisplayName, Email, PhoneNumber, PasswordHash, IsActive, IsSystem, NotifySms, SmsOnEveryNewSignIn,
                                Address, FatherName, BirthDate, CreatedAtUtc, CreatedByUserId)
SELECT p.UserId, @Tenant, p.Username, p.First, p.Last, p.First + N' ' + p.Last, NULL, p.Phone, @PwHash, 1, 0, 1, 0,
       N'تهران، خیابان ولیعصر، برج فناوری پارس، طبقهٔ ۸', CHOOSE(p.G % 6 + 1, N'محمد', N'علی', N'حسن', N'رضا', N'احمد', N'مهدی'),
       DATEFROMPARTS(1975 + p.G % 20, 1 + p.G % 12, 1 + p.G % 28), @Now, @Admin
FROM #Person p;

INSERT INTO [identity].[UserRoles] (UserId, RoleId)
SELECT p.UserId, CASE WHEN p.IsManager = 1 THEN @RoleManager ELSE @RoleStaff END FROM #Person p;

INSERT INTO [identity].[UserGroups] (Id, TenantId, Name, NormalizedName, Description, IsActive, OwnerUserId, CreatedAtUtc, CreatedByUserId)
SELECT t.GroupId, @Tenant, t.TeamName, UPPER(t.TeamName), NULL, 1, m.UserId, @Now, m.UserId
FROM #Team t JOIN #Person m ON m.TeamIdx = t.TeamIdx AND m.IsManager = 1;

INSERT INTO [identity].[UserGroupMembers] (UserGroupId, UserId, JoinedAtUtc)
SELECT t.GroupId, p.UserId, @Now FROM #Team t JOIN #Person p ON p.TeamIdx = t.TeamIdx;

------------------------------------------------------------------------------------------------ organization chart
DECLARE @Company uniqueidentifier = NEWID();
DECLARE @PosManager uniqueidentifier = NEWID(), @PosSenior uniqueidentifier = NEWID(), @PosStaff uniqueidentifier = NEWID();
DECLARE @NodeTypeUnit uniqueidentifier = (SELECT TOP (1) Id FROM org_structure.NodeTypes WHERE TenantId = @Tenant AND Code = N'unit');
IF @NodeTypeUnit IS NULL PRINT N'  note: no node type with code unit; units are made without a node type.';

INSERT INTO org_structure.Companies (Id, TenantId, Name, Code, Description, ManagerUserId, IsActive, UiLevel, CreatedAtUtc, CreatedByUserId)
VALUES (@Company, @Tenant, N'شرکت فناوران پارس', N'FP', N'شرکت نرم‌افزاری فعال در حوزهٔ مدیریت پروژه و فرآیند', NULL, 1, 0, @Now, @Admin);

INSERT INTO org_structure.Positions (Id, TenantId, Title, ParentPositionId, IsActive, CreatedAtUtc, CreatedByUserId)
VALUES (@PosManager, @Tenant, N'مدیر واحد', NULL, 1, @Now, @Admin),
       (@PosSenior, @Tenant, N'کارشناس ارشد', @PosManager, 1, @Now, @Admin),
       (@PosStaff, @Tenant, N'کارشناس', @PosManager, 1, @Now, @Admin);

INSERT INTO org_structure.PositionCompanies (Id, TenantId, PositionId, CompanyId, CreatedAtUtc, CreatedByUserId)
VALUES (NEWID(), @Tenant, @PosManager, @Company, @Now, @Admin),
       (NEWID(), @Tenant, @PosSenior, @Company, @Now, @Admin),
       (NEWID(), @Tenant, @PosStaff, @Company, @Now, @Admin);

INSERT INTO organization.OrganizationUnits (Id, TenantId, Name, ManagerUserId, IsActive, NodeTypeId, SortOrder, CreatedAtUtc, CreatedByUserId)
SELECT t.UnitId, @Tenant, t.UnitName, NULL, 1, @NodeTypeUnit, t.TeamIdx, @Now, @Admin FROM #Team t;

INSERT INTO org_structure.CompanyUnits (Id, TenantId, CompanyId, UnitId, CreatedAtUtc, CreatedByUserId)
SELECT NEWID(), @Tenant, @Company, t.UnitId, @Now, @Admin FROM #Team t;

-- Posts: the manager's post of each unit, and one post per staff member under it (codes are the company's numbers).
CREATE TABLE #Post (PostId uniqueidentifier NOT NULL DEFAULT NEWID(), G int NOT NULL, TeamIdx int NOT NULL, IsManager bit NOT NULL, PositionId uniqueidentifier NOT NULL,
                    Code int NOT NULL, SortOrder int NOT NULL DEFAULT 0, ParentPostId uniqueidentifier NULL);
INSERT INTO #Post (G, TeamIdx, IsManager, PositionId, Code)
SELECT p.G, p.TeamIdx, p.IsManager, CASE WHEN p.IsManager = 1 THEN @PosManager WHEN p.Title LIKE N'%ارشد%' THEN @PosSenior ELSE @PosStaff END,
       ROW_NUMBER() OVER (ORDER BY p.TeamIdx, p.IsManager DESC, p.G)
FROM #Person p;
UPDATE po SET ParentPostId = mp.PostId
FROM #Post po JOIN #Post mp ON mp.TeamIdx = po.TeamIdx AND mp.IsManager = 1 WHERE po.IsManager = 0;
UPDATE po SET SortOrder = po.Code - x.MinCode
FROM #Post po JOIN (SELECT TeamIdx, MIN(Code) AS MinCode FROM #Post GROUP BY TeamIdx) x ON x.TeamIdx = po.TeamIdx;

INSERT INTO org_structure.UnitPositions (Id, TenantId, UnitId, PositionId, SortOrder, Code, CompanyId, ParentPostId, IsCompanyHead, IsManagerPost, CreatedAtUtc, CreatedByUserId)
SELECT po.PostId, @Tenant, t.UnitId, po.PositionId, po.SortOrder, CAST(po.Code AS nvarchar(40)), @Company, po.ParentPostId, 0, 0, @Now, @Admin
FROM #Post po JOIN #Team t ON t.TeamIdx = po.TeamIdx;

INSERT INTO org_structure.UserAssignments (Id, TenantId, UserId, CompanyId, UnitId, PositionId, ManagerUserId, CreatedAtUtc, CreatedByUserId)
SELECT NEWID(), @Tenant, p.UserId, @Company, t.UnitId, po.PositionId, CASE WHEN p.IsManager = 1 THEN NULL ELSE m.UserId END, @Now, @Admin
FROM #Person p
JOIN #Team t ON t.TeamIdx = p.TeamIdx
JOIN #Post po ON po.G = p.G
LEFT JOIN #Person m ON m.TeamIdx = p.TeamIdx AND m.IsManager = 1;

INSERT INTO org_structure.UserPositions (Id, TenantId, UserId, UnitPositionId, CreatedAtUtc, CreatedByUserId)
SELECT NEWID(), @Tenant, p.UserId, po.PostId, @Now, @Admin FROM #Person p JOIN #Post po ON po.G = p.G;

------------------------------------------------------------------------------------------------ tags
DECLARE @ProjectTag uniqueidentifier = NEWID();
INSERT INTO task_management.Tags (Id, TenantId, Name, NormalizedName, Color, CreatedAtUtc, CreatedByUserId)
SELECT TagId, @Tenant, Txt, UPPER(Txt), NULL, @Now, @Admin FROM #Tag;
INSERT INTO task_management.Tags (Id, TenantId, Name, NormalizedName, Color, CreatedAtUtc, CreatedByUserId)
VALUES (@ProjectTag, @Tenant, N'پروژه', N'پروژه', NULL, @Now, @Admin);

------------------------------------------------------------------------------------------------ tasks
CREATE TABLE #Staff (G int NOT NULL PRIMARY KEY, TeamIdx int NOT NULL, K int NOT NULL, Cnt int NOT NULL);
INSERT INTO #Staff (G, TeamIdx, K, Cnt)
SELECT G, TeamIdx, ROW_NUMBER() OVER (PARTITION BY TeamIdx ORDER BY G), COUNT(*) OVER (PARTITION BY TeamIdx) FROM #Person WHERE IsManager = 0;

CREATE TABLE #Seed (Seq int IDENTITY(1, 1) PRIMARY KEY, TeamIdx int NOT NULL, Title nvarchar(200) NOT NULL, Descr nvarchar(4000) NULL, RespG int NULL);
INSERT INTO #Seed (TeamIdx, Title, Descr) SELECT TeamIdx, Title, Descr FROM #Curated;
UPDATE s SET RespG = st.G
FROM #Seed s JOIN #Staff st ON st.TeamIdx = s.TeamIdx AND st.K = (s.Seq * 7) % st.Cnt + 1
WHERE s.RespG IS NULL;

INSERT INTO #Seed (TeamIdx, Title, Descr, RespG)
SELECT p.TeamIdx,
       v.Txt + N' ' + o.Txt + N' برای ' + c.Txt + N' [' + CAST(p.G AS nvarchar(10)) + N'-' + CAST(n.n AS nvarchar(10)) + N']',
       d.Txt, p.G
FROM #Person p
JOIN #Num n ON n.n <= @TasksPerPerson
JOIN (SELECT TeamIdx, Idx, Txt, cnt = COUNT(*) OVER (PARTITION BY TeamIdx) FROM #Verb) v ON v.TeamIdx = p.TeamIdx AND v.Idx = (p.G * 3 + n.n * 5) % v.cnt + 1
JOIN (SELECT TeamIdx, Idx, Txt, cnt = COUNT(*) OVER (PARTITION BY TeamIdx) FROM #Obj) o ON o.TeamIdx = p.TeamIdx AND o.Idx = (p.G * 5 + n.n * 7) % o.cnt + 1
JOIN #Client c ON c.Idx = (p.G * 7 + n.n * 3) % @nC + 1
LEFT JOIN #GDesc d ON d.Idx = (p.G * 11 + n.n * 13) % ((SELECT COUNT(*) FROM #GDesc) + 3) + 1
WHERE p.IsManager = 0;

-- Everything below is fixed by the task's number (CHECKSUM), so a run is repeatable. Fridays take no due date (as the API).
SELECT s.Seq, s.TeamIdx, TaskId = NEWID(), s.Title, s.Descr, RespUserId = r.UserId,
       OwnerUserId = CASE WHEN h1.v < 65 THEN m.UserId ELSE r.UserId END, GroupId = t.GroupId,
       Status = CHOOSE(h2.v + 1, 0, 0, 0, 1, 1, 2, 3, 3), Priority = CHOOSE(h3.v + 1, 2, 1, 1, 0),
       DueDate = f.d, DueTime = CASE WHEN h5.v < 40 THEN CAST(CHOOSE(h5.v % 4 + 1, '10:00', '12:00', '14:30', '16:00') AS time) END,
       CreatedAtUtc = cr.c, DoneAt = dn.v, ModifiedAtUtc = CASE WHEN CHOOSE(h2.v + 1, 0, 0, 0, 1, 1, 2, 3, 3) = 3 THEN dn.v ELSE md.v END
INTO #Task
FROM #Seed s
JOIN #Person r ON r.G = s.RespG
JOIN #Person m ON m.TeamIdx = s.TeamIdx AND m.IsManager = 1
JOIN #Team t ON t.TeamIdx = s.TeamIdx
CROSS APPLY (SELECT v = (CHECKSUM(s.Seq, 1) & 2147483647) % 100) h1
CROSS APPLY (SELECT v = (CHECKSUM(s.Seq, 2) & 2147483647) % 8) h2
CROSS APPLY (SELECT v = (CHECKSUM(s.Seq, 3) & 2147483647) % 4) h3
CROSS APPLY (SELECT v = (CHECKSUM(s.Seq, 5) & 2147483647) % 100) h5
CROSS APPLY (SELECT d = DATEADD(day, (CHECKSUM(s.Seq, 4) & 2147483647) % 56 - 14, @Today)) d1
CROSS APPLY (SELECT d = CASE WHEN DATEDIFF(day, '19000107', d1.d) % 7 = 5 THEN DATEADD(day, 1, d1.d) ELSE d1.d END) f
CROSS APPLY (SELECT c = DATEADD(minute, -((CHECKSUM(s.Seq, 6) & 2147483647) % 43200) - 60, @Now)) cr
CROSS APPLY (SELECT raw = DATEADD(minute, (CHECKSUM(s.Seq, 7) & 2147483647) % 5760 + 60, cr.c)) dr
CROSS APPLY (SELECT v = CASE WHEN dr.raw > @Now THEN @Now ELSE dr.raw END) dn
CROSS APPLY (SELECT raw = DATEADD(minute, (CHECKSUM(s.Seq, 8) & 2147483647) % 2880 + 30, cr.c)) mr
CROSS APPLY (SELECT v = CASE WHEN mr.raw > @Now THEN @Now ELSE mr.raw END) md;

INSERT INTO task_management.Tasks (Id, TenantId, Title, Description, DueDate, DueTime, IsProject, Priority, Status, OwnerUserId, OwnerHeldByPost, AssignedUserId, AssignedUserGroupId,
                                   AllowAssigneeStatusUpdate, ReviewStatus, ActualCompletionDateUtc, CreatedAtUtc, CreatedByUserId, ModifiedAtUtc, ModifiedByUserId)
SELECT TaskId, @Tenant, Title, Descr, DueDate, DueTime, 0, Priority, Status, OwnerUserId, 0, RespUserId, GroupId,
       1, 0, CASE WHEN Status = 3 THEN DoneAt END, CreatedAtUtc, OwnerUserId, ModifiedAtUtc, OwnerUserId
FROM #Task;

-- Each team's manager, once (a lookup per task row would scan #Person for every one).
SELECT TeamIdx, ManagerId = MIN(CAST(UserId AS char(36))) INTO #TeamManagerRaw FROM #Person WHERE IsManager = 1 GROUP BY TeamIdx;
SELECT TeamIdx, ManagerId = CAST(ManagerId AS uniqueidentifier) INTO #TeamManager FROM #TeamManagerRaw;
CREATE UNIQUE CLUSTERED INDEX IX_TeamManager ON #TeamManager (TeamIdx);

-- Who can see a task: everyone of the team (access); the responsible person is flagged.
-- A task the team's manager is responsible for is not shared with the staff below them:
-- only the manager and the task's owner are on its list.
INSERT INTO task_management.TaskAssignees (Id, TaskId, UserId, IsResponsible, IsActing)
SELECT NEWID(), t.TaskId, p.UserId, CASE WHEN p.UserId = t.RespUserId THEN 1 ELSE 0 END, 0
FROM #Task t
JOIN #TeamManager tm ON tm.TeamIdx = t.TeamIdx
JOIN #Person p ON p.TeamIdx = t.TeamIdx
WHERE t.RespUserId <> tm.ManagerId OR p.UserId = t.RespUserId OR p.UserId = t.OwnerUserId;

-- One or two tags per task, from the team's own.
INSERT INTO task_management.TaskTags (Id, TaskId, TagId)
SELECT NEWID(), t.TaskId, g.TagId
FROM #Task t
JOIN (SELECT TeamIdx, Idx, TagId, cnt = COUNT(*) OVER (PARTITION BY TeamIdx) FROM #Tag) g ON g.TeamIdx = t.TeamIdx AND g.Idx = t.Seq % g.cnt + 1;
INSERT INTO task_management.TaskTags (Id, TaskId, TagId)
SELECT NEWID(), t.TaskId, g.TagId
FROM #Task t
JOIN (SELECT TeamIdx, Idx, TagId, cnt = COUNT(*) OVER (PARTITION BY TeamIdx) FROM #Tag) g ON g.TeamIdx = t.TeamIdx AND g.Idx = (t.Seq + 1) % g.cnt + 1 AND g.cnt > 1
WHERE (CHECKSUM(t.Seq, 9) & 2147483647) % 100 < 50;

-- Comments: 45% of the tasks get one, 20% a second, 6% a third; by the responsible person or the owner, after the task was made.
INSERT INTO task_management.TaskComments (Id, TenantId, TaskId, UserId, Text, CreatedAtUtc, CreatedByUserId)
SELECT NEWID(), @Tenant, t.TaskId, u.id, c.Txt, cat.v, u.id
FROM #Task t
JOIN (VALUES (1), (2), (3)) n (n) ON 1 = 1
CROSS APPLY (SELECT v = (CHECKSUM(t.Seq, 20) & 2147483647) % 100) h
CROSS APPLY (SELECT id = CASE WHEN n.n % 2 = 1 THEN t.RespUserId ELSE t.OwnerUserId END) u
CROSS APPLY (SELECT raw = DATEADD(minute, n.n * 120 + (CHECKSUM(t.Seq, n.n, 21) & 2147483647) % 600, t.CreatedAtUtc)) ar
CROSS APPLY (SELECT v = CASE WHEN ar.raw > @Now THEN @Now ELSE ar.raw END) cat
JOIN #Comment c ON c.Idx = (CHECKSUM(t.Seq, n.n, 22) & 2147483647) % @nCm + 1
WHERE (n.n = 1 AND h.v < 45) OR (n.n = 2 AND h.v < 20) OR (n.n = 3 AND h.v < 6);

------------------------------------------------------------------------------------------------ projects with subtasks
DECLARE @ProjOwner TABLE (Idx int PRIMARY KEY, ManagerId uniqueidentifier, ManagerName nvarchar(200), GroupId uniqueidentifier, StartDate date, EndDate date);
INSERT INTO @ProjOwner (Idx, ManagerId, ManagerName, GroupId, StartDate, EndDate)
SELECT pr.Idx, m.UserId, m.First + N' ' + m.Last, t.GroupId, DATEADD(day, 1 + pr.Idx % 3, @Today), e.d
FROM #Proj pr
JOIN #Team t ON t.TeamIdx = pr.TeamIdx
JOIN #Person m ON m.TeamIdx = pr.TeamIdx AND m.IsManager = 1
CROSS APPLY (SELECT d = DATEADD(day, 24 + pr.Idx * 3, @Today)) e0
CROSS APPLY (SELECT d = CASE WHEN DATEDIFF(day, '19000107', e0.d) % 7 = 5 THEN DATEADD(day, 1, e0.d) ELSE e0.d END) e;

INSERT INTO task_management.Tasks (Id, TenantId, Title, Description, DueDate, IsProject, Priority, Status, OwnerUserId, OwnerHeldByPost, AssignedUserId, AssignedUserGroupId,
                                   AllowAssigneeStatusUpdate, ReviewStatus, CharterDescription, CharterProjectManager, CharterStartDate, CharterEndDate, CreatedAtUtc, CreatedByUserId)
SELECT pr.TaskId, @Tenant, pr.Title, N'پروژهٔ اصلی ' + tm.TeamName + N' در این فصل.', po.EndDate, 1, 2, 1, po.ManagerId, 0, po.ManagerId, po.GroupId,
       1, 0, N'هدف: ' + REPLACE(pr.Title, N'پروژهٔ ', N'') + N' تا پایان فصل.', po.ManagerName, po.StartDate, po.EndDate, DATEADD(day, -7, @Now), po.ManagerId
FROM #Proj pr JOIN @ProjOwner po ON po.Idx = pr.Idx JOIN #Team tm ON tm.TeamIdx = pr.TeamIdx;

INSERT INTO task_management.TaskAssignees (Id, TaskId, UserId, IsResponsible, IsActing)
SELECT NEWID(), pr.TaskId, p.UserId, CASE WHEN p.UserId = po.ManagerId THEN 1 ELSE 0 END, 0
FROM #Proj pr JOIN @ProjOwner po ON po.Idx = pr.Idx JOIN #Person p ON p.TeamIdx = pr.TeamIdx
-- A project is the manager's: of the staff only those with a step of it on their plate see it.
WHERE p.UserId = po.ManagerId
   OR EXISTS (SELECT 1 FROM #Step s JOIN #Staff sf ON sf.TeamIdx = pr.TeamIdx AND sf.K = s.Ord % sf.Cnt + 1
              WHERE s.ProjIdx = pr.Idx AND sf.G = p.G);

INSERT INTO task_management.TaskTags (Id, TaskId, TagId) SELECT NEWID(), TaskId, @ProjectTag FROM #Proj;

INSERT INTO task_management.SubTasks (Id, TenantId, TaskId, Title, Importance, SortOrder, StartDate, EndDate, IsCompleted, IsGeneratedOccurrence, Status,
                                      ActualStartedAtUtc, ActualCompletedAtUtc, CreatedAtUtc, CreatedByUserId)
SELECT s.SubTaskId, @Tenant, pr.TaskId, s.Title, CASE WHEN s.Ord = 1 THEN 2 ELSE 1 END, s.Ord - 1,
       DATEADD(day, (s.Ord - 1) * 4 + 1, @Today), DATEADD(day, s.Ord * 4, @Today),
       CASE WHEN st.v = 2 THEN 1 ELSE 0 END, 0, st.v,
       CASE WHEN st.v >= 1 THEN DATEADD(day, -(8 - s.Ord), @Now) END, CASE WHEN st.v = 2 THEN DATEADD(day, -(7 - s.Ord), @Now) END,
       DATEADD(day, -7, @Now), po.ManagerId
FROM #Step s
JOIN #Proj pr ON pr.Idx = s.ProjIdx
JOIN @ProjOwner po ON po.Idx = pr.Idx
CROSS APPLY (SELECT cnt = (SELECT COUNT(*) FROM #Step x WHERE x.ProjIdx = s.ProjIdx)) k
CROSS APPLY (SELECT done = (pr.Idx * 7) % (k.cnt - 1)) d
CROSS APPLY (SELECT v = CASE WHEN s.Ord <= d.done THEN 2 WHEN s.Ord = d.done + 1 AND pr.Idx % 2 = 0 THEN 1 ELSE 0 END) st;

INSERT INTO task_management.SubTaskAssignees (Id, SubTaskId, UserId)
SELECT NEWID(), s.SubTaskId, p.UserId
FROM #Step s
JOIN #Proj pr ON pr.Idx = s.ProjIdx
JOIN #Staff sf ON sf.TeamIdx = pr.TeamIdx AND sf.K = s.Ord % sf.Cnt + 1
JOIN #Person p ON p.G = sf.G;

------------------------------------------------------------------------------------------------ personal notes
INSERT INTO task_management.Notes (Id, TenantId, UserId, Title, Content, Color, IsPinned, CreatedAtUtc, CreatedByUserId)
SELECT NEWID(), @Tenant, p.UserId, nt.Title, nt.Content, nt.Color, nt.Pinned,
       DATEADD(minute, -((CHECKSUM(p.G, nt.Idx, 30) & 2147483647) % 20000), @Now), p.UserId
FROM #Person p
JOIN #Note nt ON (p.IsManager = 1 AND nt.Idx <= 3) OR (p.IsManager = 0 AND (CHECKSUM(p.G, nt.Idx, 31) & 2147483647) % 100 < 30);

------------------------------------------------------------------------------------------------ foreign keys back on, then check
DECLARE fkc2 CURSOR LOCAL FAST_FORWARD FOR SELECT Enable_ FROM #Fk;
OPEN fkc2; FETCH NEXT FROM fkc2 INTO @sql;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC (@sql); FETCH NEXT FROM fkc2 INTO @sql; END;
CLOSE fkc2; DEALLOCATE fkc2;

COMMIT TRANSACTION;

------------------------------------------------------------------------------------------------ summary
SELECT N'users (without admin)' AS What, COUNT(*) AS N FROM [identity].[Users] WHERE Id <> @Admin
UNION ALL SELECT N'teams', COUNT(*) FROM [identity].[UserGroups]
UNION ALL SELECT N'units', COUNT(*) FROM organization.OrganizationUnits
UNION ALL SELECT N'posts', COUNT(*) FROM org_structure.UnitPositions
UNION ALL SELECT N'tasks', COUNT(*) FROM task_management.Tasks WHERE IsProject = 0
UNION ALL SELECT N'  of them overdue (open, due before today)', COUNT(*) FROM task_management.Tasks WHERE IsProject = 0 AND Status IN (0, 1) AND DueDate < @Today
UNION ALL SELECT N'projects', COUNT(*) FROM task_management.Tasks WHERE IsProject = 1
UNION ALL SELECT N'subtasks', COUNT(*) FROM task_management.SubTasks
UNION ALL SELECT N'task people rows', COUNT(*) FROM task_management.TaskAssignees
UNION ALL SELECT N'comments', COUNT(*) FROM task_management.TaskComments
UNION ALL SELECT N'notes', COUNT(*) FROM task_management.Notes;

-- Who can sign in (password Test@Pass#2026): the managers first.
SELECT p.Username, p.First + N' ' + p.Last AS Name, t.TeamName AS Team, p.Title, CASE WHEN p.IsManager = 1 THEN N'manager' ELSE N'' END AS Manager
FROM #Person p JOIN #Team t ON t.TeamIdx = p.TeamIdx
ORDER BY p.IsManager DESC, p.TeamIdx, p.G;
