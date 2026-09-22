using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusCore.Infrastructure.Migrations
{
    /// <summary>
    /// Sign-in by username or mobile number, and Telegram removed.
    ///
    /// * Mobile numbers are brought into the one canonical form (Domain.Identity.PhoneNumber) and
    ///   become unique per tenant, like usernames. A number that cannot be recognised is left as
    ///   it is - never deleted - and simply does not work for signing in until it is corrected.
    ///   If two users of a tenant turn out to share a number, the migration stops (and changes
    ///   nothing) with a message saying so: which account keeps it is a decision for a person.
    /// * Email becomes optional (a contact address for password-reset links, no longer a
    ///   sign-in name); still unique when present.
    /// * identity.Users loses TelegramChatId and NotifyTelegram, and the stored notification
    ///   channel settings lose their Telegram section (including the encrypted bot token).
    /// </summary>
    public partial class SignInByUsernameOrPhoneAndRemoveTelegram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(NormalizePhoneNumbersSql);
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [identity].[Users] WHERE [PhoneNumber] IS NOT NULL
                           GROUP BY [TenantId], [PhoneNumber] HAVING COUNT(*) > 1)
                    THROW 50001, N'Two or more users of the same tenant have the same mobile number. Give each of them a different number (or clear the duplicate), then run this migration again. Nothing was changed.', 1;
                """);
            migrationBuilder.Sql(
                """
                UPDATE [platform].[Settings]
                SET [Value] = JSON_MODIFY([Value], '$.telegram', NULL)
                WHERE [Key] = N'Notifications.Channels' AND ISJSON([Value]) = 1 AND JSON_QUERY([Value], '$.telegram') IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_Email",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyTelegram",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                schema: "identity",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "identity",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email",
                schema: "identity",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_PhoneNumber",
                schema: "identity",
                table: "Users",
                columns: new[] { "TenantId", "PhoneNumber" },
                unique: true,
                filter: "[PhoneNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old schema needs an email for every user. Accounts created without one cannot be
            // given a made-up address, so rolling back stops instead.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [identity].[Users] WHERE [Email] IS NULL)
                    THROW 50002, N'Cannot roll back: some users have no email address, which the previous version requires. Add one for each of them first.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_Email",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_PhoneNumber",
                schema: "identity",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "identity",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyTelegram",
                schema: "identity",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramChatId",
                schema: "identity",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email",
                schema: "identity",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true);
        }

        /// <summary>
        /// The SQL twin of Domain.Identity.PhoneNumber.Normalize: Persian/Arabic digits and
        /// separators first, then 09xxxxxxxxx for Iranian mobiles and +&lt;digits&gt; for numbers
        /// written with another country code. Plain REPLACE (not TRANSLATE) so it also runs on
        /// SQL Server 2016.
        /// </summary>
        internal static string NormalizePhoneNumbersSql
        {
            get
            {
                var cleaned = "u.[PhoneNumber]";
                for (var d = 0; d <= 9; d++)
                {
                    cleaned = $"REPLACE({cleaned}, NCHAR({0x06F0 + d}), N'{d}')"; // Persian digits
                    cleaned = $"REPLACE({cleaned}, NCHAR({0x0660 + d}), N'{d}')"; // Arabic-Indic digits
                }

                foreach (var separator in new[] { "N' '", "N'-'", "N'('", "N')'", "N'.'", "NCHAR(8204)", "NCHAR(8206)", "NCHAR(8207)", "NCHAR(160)" })
                {
                    cleaned = $"REPLACE({cleaned}, {separator}, N'')";
                }

                return $"""
                    UPDATE u SET [PhoneNumber] = n.[Canonical]
                    FROM [identity].[Users] AS u
                    CROSS APPLY (SELECT {cleaned} AS [V]) AS c
                    CROSS APPLY (SELECT CASE
                        WHEN c.[V] LIKE N'+980%' THEN SUBSTRING(c.[V], 4, 40)
                        WHEN c.[V] LIKE N'+98%' THEN N'0' + SUBSTRING(c.[V], 4, 40)
                        WHEN c.[V] LIKE N'00980%' THEN SUBSTRING(c.[V], 5, 40)
                        WHEN c.[V] LIKE N'0098%' THEN N'0' + SUBSTRING(c.[V], 5, 40)
                        WHEN c.[V] LIKE N'989%' AND LEN(c.[V]) = 12 THEN N'0' + SUBSTRING(c.[V], 3, 40)
                        WHEN c.[V] LIKE N'9%' AND LEN(c.[V]) = 10 THEN N'0' + c.[V]
                        ELSE c.[V] END AS [Iranian]) AS i
                    CROSS APPLY (SELECT CASE
                        WHEN i.[Iranian] LIKE N'09[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]' THEN i.[Iranian]
                        WHEN c.[V] LIKE N'+[1-9]%' AND c.[V] NOT LIKE N'+98%' AND LEN(c.[V]) BETWEEN 9 AND 16
                             AND SUBSTRING(c.[V], 2, 40) NOT LIKE N'%[^0-9]%' THEN c.[V]
                        WHEN c.[V] LIKE N'00[1-9]%' AND c.[V] NOT LIKE N'0098%' AND LEN(c.[V]) BETWEEN 10 AND 17
                             AND c.[V] NOT LIKE N'%[^0-9]%' THEN N'+' + SUBSTRING(c.[V], 3, 40)
                        ELSE NULL END AS [Canonical]) AS n
                    WHERE u.[PhoneNumber] IS NOT NULL AND n.[Canonical] IS NOT NULL AND n.[Canonical] <> u.[PhoneNumber];
                    """;
            }
        }
    }
}
