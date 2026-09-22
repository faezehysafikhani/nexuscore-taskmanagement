using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusCore.Infrastructure.Migrations
{
    /// <summary>
    /// First and last name on users; the built-in administrator flagged as the system account
    /// (it cannot be deleted, disabled, re-roled or renamed through the API); permissions granted
    /// directly to a user (identity.UserPermissions, mirroring RolePermissions).
    /// </summary>
    public partial class UserNamesSystemAccountAndDirectPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                schema: "identity",
                table: "Users",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                schema: "identity",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                schema: "identity",
                table: "Users",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => new { x.UserId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_UserPermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalSchema: "identity",
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPermissions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The account the seeder creates (DefaultDataSeeder.AdminUserId) is the system account.
            migrationBuilder.Sql("UPDATE [identity].[Users] SET [IsSystem] = 1 WHERE [Id] = '33333333-3333-3333-3333-333333333333';");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_PermissionId",
                schema: "identity",
                table: "UserPermissions",
                column: "PermissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPermissions",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "FirstName",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastName",
                schema: "identity",
                table: "Users");
        }
    }
}
