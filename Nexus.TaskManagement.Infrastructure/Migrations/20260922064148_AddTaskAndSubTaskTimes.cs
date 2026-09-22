using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskAndSubTaskTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "DueTime",
                schema: "task_management",
                table: "Tasks",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                schema: "task_management",
                table: "SubTasks",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                schema: "task_management",
                table: "SubTasks",
                type: "time",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueTime",
                schema: "task_management",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EndTime",
                schema: "task_management",
                table: "SubTasks");

            migrationBuilder.DropColumn(
                name: "StartTime",
                schema: "task_management",
                table: "SubTasks");
        }
    }
}
