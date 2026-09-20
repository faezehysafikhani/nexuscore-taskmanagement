using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitTaskManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "task_management");

            migrationBuilder.CreateTable(
                name: "Files",
                schema: "task_management",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FileSizeBytes = table.Column<int>(type: "int", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                    table.CheckConstraint("CK_Files_MaxSize", "[FileSizeBytes] > 0 AND [FileSizeBytes] <= 204800");
                    table.ForeignKey(
                        name: "FK_Files_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                schema: "task_management",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notes_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                schema: "task_management",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                schema: "task_management",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsProject = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ActualCompletionDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedUserGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AllowAssigneeStatusUpdate = table.Column<bool>(type: "bit", nullable: false),
                    CharterDescription = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CharterProjectManager = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CharterStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CharterEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_UserGroups_AssignedUserGroupId",
                        column: x => x.AssignedUserGroupId,
                        principalSchema: "identity",
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Users_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepetitiveTasks",
                schema: "task_management",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Frequency = table.Column<int>(type: "int", nullable: false),
                    IntervalWeeks = table.Column<int>(type: "int", nullable: true),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    WeeklyDays = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MonthlyDays = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NthOccurrence = table.Column<int>(type: "int", nullable: true),
                    NthWeekday = table.Column<int>(type: "int", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NextExecutionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastExecutionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepetitiveTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepetitiveTasks_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "task_management",
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubTasks",
                schema: "task_management",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Importance = table.Column<int>(type: "int", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubTasks_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "task_management",
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskAssignees",
                schema: "task_management",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAssignees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskAssignees_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "task_management",
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskAssignees_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskFiles",
                schema: "task_management",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskFiles", x => x.Id);
                    table.CheckConstraint("CK_TaskFiles_ExactlyOneOwner", "(CASE WHEN [TaskId] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [SubTaskId] IS NOT NULL THEN 1 ELSE 0 END) = 1");
                    table.ForeignKey(
                        name: "FK_TaskFiles_Files_FileId",
                        column: x => x.FileId,
                        principalSchema: "task_management",
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskFiles_SubTasks_SubTaskId",
                        column: x => x.SubTaskId,
                        principalSchema: "task_management",
                        principalTable: "SubTasks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaskFiles_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "task_management",
                        principalTable: "Tasks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TaskTags",
                schema: "task_management",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTags", x => x.Id);
                    table.CheckConstraint("CK_TaskTags_AtLeastOneOwner", "[TaskId] IS NOT NULL OR [SubTaskId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_TaskTags_SubTasks_SubTaskId",
                        column: x => x.SubTaskId,
                        principalSchema: "task_management",
                        principalTable: "SubTasks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaskTags_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "task_management",
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskTags_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "task_management",
                        principalTable: "Tasks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Files_TenantId_StoredFileName",
                schema: "task_management",
                table: "Files",
                columns: new[] { "TenantId", "StoredFileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Files_UploadedByUserId",
                schema: "task_management",
                table: "Files",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_TenantId_UserId_IsPinned",
                schema: "task_management",
                table: "Notes",
                columns: new[] { "TenantId", "UserId", "IsPinned" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_UserId",
                schema: "task_management",
                table: "Notes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RepetitiveTasks_TaskId",
                schema: "task_management",
                table: "RepetitiveTasks",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepetitiveTasks_TenantId_IsActive_NextExecutionAtUtc",
                schema: "task_management",
                table: "RepetitiveTasks",
                columns: new[] { "TenantId", "IsActive", "NextExecutionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubTasks_TaskId_SortOrder",
                schema: "task_management",
                table: "SubTasks",
                columns: new[] { "TaskId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SubTasks_TenantId_IsCompleted",
                schema: "task_management",
                table: "SubTasks",
                columns: new[] { "TenantId", "IsCompleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_TenantId_NormalizedName",
                schema: "task_management",
                table: "Tags",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignees_TaskId_UserId",
                schema: "task_management",
                table: "TaskAssignees",
                columns: new[] { "TaskId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignees_UserId",
                schema: "task_management",
                table: "TaskAssignees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskFiles_FileId_SubTaskId",
                schema: "task_management",
                table: "TaskFiles",
                columns: new[] { "FileId", "SubTaskId" },
                unique: true,
                filter: "[SubTaskId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskFiles_FileId_TaskId",
                schema: "task_management",
                table: "TaskFiles",
                columns: new[] { "FileId", "TaskId" },
                unique: true,
                filter: "[TaskId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskFiles_SubTaskId",
                schema: "task_management",
                table: "TaskFiles",
                column: "SubTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskFiles_TaskId",
                schema: "task_management",
                table: "TaskFiles",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedUserGroupId",
                schema: "task_management",
                table: "Tasks",
                column: "AssignedUserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedUserId",
                schema: "task_management",
                table: "Tasks",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerUserId",
                schema: "task_management",
                table: "Tasks",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_AssignedUserGroupId",
                schema: "task_management",
                table: "Tasks",
                columns: new[] { "TenantId", "AssignedUserGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_AssignedUserId",
                schema: "task_management",
                table: "Tasks",
                columns: new[] { "TenantId", "AssignedUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_DueDate",
                schema: "task_management",
                table: "Tasks",
                columns: new[] { "TenantId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_IsProject",
                schema: "task_management",
                table: "Tasks",
                columns: new[] { "TenantId", "IsProject" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_Status",
                schema: "task_management",
                table: "Tasks",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskTags_SubTaskId",
                schema: "task_management",
                table: "TaskTags",
                column: "SubTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTags_TagId_SubTaskId",
                schema: "task_management",
                table: "TaskTags",
                columns: new[] { "TagId", "SubTaskId" },
                unique: true,
                filter: "[SubTaskId] IS NOT NULL AND [TaskId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTags_TagId_TaskId",
                schema: "task_management",
                table: "TaskTags",
                columns: new[] { "TagId", "TaskId" },
                unique: true,
                filter: "[TaskId] IS NOT NULL AND [SubTaskId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTags_TaskId",
                schema: "task_management",
                table: "TaskTags",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notes",
                schema: "task_management");

            migrationBuilder.DropTable(
                name: "RepetitiveTasks",
                schema: "task_management");

            migrationBuilder.DropTable(
                name: "TaskAssignees",
                schema: "task_management");

            migrationBuilder.DropTable(
                name: "TaskFiles",
                schema: "task_management");

            migrationBuilder.DropTable(
                name: "TaskTags",
                schema: "task_management");

            migrationBuilder.DropTable(
                name: "Files",
                schema: "task_management");

            migrationBuilder.DropTable(
                name: "SubTasks",
                schema: "task_management");

            migrationBuilder.DropTable(
                name: "Tags",
                schema: "task_management");

            migrationBuilder.DropTable(
                name: "Tasks",
                schema: "task_management");
        }
    }
}
