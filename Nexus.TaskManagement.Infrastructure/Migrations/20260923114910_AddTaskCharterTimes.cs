using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.TaskManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCharterTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "CharterEndTime",
                schema: "task_management",
                table: "Tasks",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "CharterStartTime",
                schema: "task_management",
                table: "Tasks",
                type: "time",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CharterEndTime",
                schema: "task_management",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "CharterStartTime",
                schema: "task_management",
                table: "Tasks");
        }
    }
}
