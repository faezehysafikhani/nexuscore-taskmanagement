using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentFilesAndGeneratedOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskFiles_ExactlyOneOwner",
                schema: "task_management",
                table: "TaskFiles");

            migrationBuilder.AddColumn<Guid>(
                name: "CommentId",
                schema: "task_management",
                table: "TaskFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGeneratedOccurrence",
                schema: "task_management",
                table: "SubTasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_TaskFiles_CommentId",
                schema: "task_management",
                table: "TaskFiles",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskFiles_FileId_CommentId",
                schema: "task_management",
                table: "TaskFiles",
                columns: new[] { "FileId", "CommentId" },
                unique: true,
                filter: "[CommentId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskFiles_ExactlyOneOwner",
                schema: "task_management",
                table: "TaskFiles",
                sql: "(CASE WHEN [TaskId] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [SubTaskId] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [CommentId] IS NOT NULL THEN 1 ELSE 0 END) = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskFiles_TaskComments_CommentId",
                schema: "task_management",
                table: "TaskFiles",
                column: "CommentId",
                principalSchema: "task_management",
                principalTable: "TaskComments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskFiles_TaskComments_CommentId",
                schema: "task_management",
                table: "TaskFiles");

            migrationBuilder.DropIndex(
                name: "IX_TaskFiles_CommentId",
                schema: "task_management",
                table: "TaskFiles");

            migrationBuilder.DropIndex(
                name: "IX_TaskFiles_FileId_CommentId",
                schema: "task_management",
                table: "TaskFiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskFiles_ExactlyOneOwner",
                schema: "task_management",
                table: "TaskFiles");

            migrationBuilder.DropColumn(
                name: "CommentId",
                schema: "task_management",
                table: "TaskFiles");

            migrationBuilder.DropColumn(
                name: "IsGeneratedOccurrence",
                schema: "task_management",
                table: "SubTasks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskFiles_ExactlyOneOwner",
                schema: "task_management",
                table: "TaskFiles",
                sql: "(CASE WHEN [TaskId] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [SubTaskId] IS NOT NULL THEN 1 ELSE 0 END) = 1");
        }
    }
}
