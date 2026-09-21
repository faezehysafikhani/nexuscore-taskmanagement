using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusCore.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileAndPersonalTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserGroups_TenantId_NormalizedName",
                schema: "identity",
                table: "UserGroups");

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                schema: "identity",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColorPalette",
                schema: "identity",
                table: "Users",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifySms",
                schema: "identity",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyTelegram",
                schema: "identity",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                schema: "identity",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramChatId",
                schema: "identity",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                schema: "identity",
                table: "Users",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeMode",
                schema: "identity",
                table: "Users",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                schema: "identity",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                schema: "identity",
                table: "UserGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Username",
                schema: "identity",
                table: "Users",
                columns: new[] { "TenantId", "Username" },
                unique: true,
                filter: "[Username] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_OwnerUserId",
                schema: "identity",
                table: "UserGroups",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_TenantId_OwnerUserId_NormalizedName",
                schema: "identity",
                table: "UserGroups",
                columns: new[] { "TenantId", "OwnerUserId", "NormalizedName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserGroups_Users_OwnerUserId",
                schema: "identity",
                table: "UserGroups",
                column: "OwnerUserId",
                principalSchema: "identity",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserGroups_Users_OwnerUserId",
                schema: "identity",
                table: "UserGroups");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_Username",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_UserGroups_OwnerUserId",
                schema: "identity",
                table: "UserGroups");

            migrationBuilder.DropIndex(
                name: "IX_UserGroups_TenantId_OwnerUserId_NormalizedName",
                schema: "identity",
                table: "UserGroups");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ColorPalette",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifySms",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyTelegram",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Theme",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ThemeMode",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Username",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                schema: "identity",
                table: "UserGroups");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_TenantId_NormalizedName",
                schema: "identity",
                table: "UserGroups",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);
        }
    }
}
