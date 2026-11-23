-- Additive upgrade only. Existing users, passwords, assignments and history remain intact.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'identity.Users', N'Address') IS NULL
    ALTER TABLE [identity].[Users] ADD [Address] nvarchar(1000) NULL;
IF COL_LENGTH(N'identity.Users', N'FatherName') IS NULL
    ALTER TABLE [identity].[Users] ADD [FatherName] nvarchar(80) NULL;
IF COL_LENGTH(N'identity.Users', N'BirthDate') IS NULL
    ALTER TABLE [identity].[Users] ADD [BirthDate] date NULL;
COMMIT;
