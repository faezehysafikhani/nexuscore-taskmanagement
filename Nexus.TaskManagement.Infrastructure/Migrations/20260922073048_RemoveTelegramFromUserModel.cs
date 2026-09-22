using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Intentionally empty. This module maps identity.Users (for its foreign keys) but does not
    /// own that table - NexusCore's SignInByUsernameOrPhoneAndRemoveTelegram migration changes it.
    /// This one only brings the module's model snapshot in line (no Telegram columns, optional
    /// email), so the next migration of the module does not pick those changes up again.
    /// </summary>
    public partial class RemoveTelegramFromUserModel : Migration
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
