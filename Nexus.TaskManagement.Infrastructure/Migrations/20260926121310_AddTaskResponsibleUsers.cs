using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskResponsibleUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsResponsible",
                schema: "task_management",
                table: "TaskAssignees",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Existing tasks keep their one responsible person, now as a responsible row.
            migrationBuilder.Sql(@"
UPDATE a SET a.IsResponsible = 1
FROM task_management.TaskAssignees a
INNER JOIN task_management.Tasks t ON t.Id = a.TaskId AND t.AssignedUserId = a.UserId;

INSERT INTO task_management.TaskAssignees (Id, TaskId, UserId, IsResponsible)
SELECT NEWID(), t.Id, t.AssignedUserId, 1
FROM task_management.Tasks t
WHERE t.AssignedUserId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM task_management.TaskAssignees a WHERE a.TaskId = t.Id AND a.UserId = t.AssignedUserId);");

            // A task's team no longer grants access by itself (its access list does). Everyone who
            // could see a team's task through the team keeps it: the team's members at this moment
            // go on the task's access list. The identity tables belong to NexusCore's migrations,
            // so this runs only where they already exist (on a new database there are no tasks).
migrationBuilder.Sql(@"
IF OBJECT_ID(N'[identity].[UserGroupMembers]', N'U') IS NOT NULL
EXEC(N'
INSERT INTO [task_management].[TaskAssignees] (Id, TaskId, UserId, IsResponsible)
SELECT NEWID(), t.Id, m.UserId, 0
FROM [task_management].[Tasks] t
INNER JOIN [identity].[UserGroupMembers] m
    ON m.UserGroupId = t.AssignedUserGroupId
WHERE t.AssignedUserGroupId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM [task_management].[TaskAssignees] a
      WHERE a.TaskId = t.Id
        AND a.UserId = m.UserId
  );');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Responsible rows added for tasks' AssignedUserId are not told apart from access rows
            // once the column is gone; they stay as access rows (AssignedUserId is unchanged).
            migrationBuilder.DropColumn(
                name: "IsResponsible",
                schema: "task_management",
                table: "TaskAssignees");
        }
    }
}
