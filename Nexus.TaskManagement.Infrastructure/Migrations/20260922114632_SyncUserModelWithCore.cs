using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Intentionally empty: brings this module's snapshot of the shared identity.Users mapping in
    /// line (first/last name, system flag). The table belongs to NexusCore, whose migration
    /// UserNamesSystemAccountAndDirectPermissions changes it.
    /// </summary>
    public partial class SyncUserModelWithCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
