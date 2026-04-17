using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace habitostracker.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupSystemMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupMessages_Users_SenderId",
                table: "GroupMessages");

            migrationBuilder.AlterColumn<int>(
                name: "SenderId",
                table: "GroupMessages",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "GroupMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMuted",
                table: "GroupMembers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeftAt",
                table: "GroupMembers",
            type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMessages_Users_SenderId",
                table: "GroupMessages",
                column: "SenderId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupMessages_Users_SenderId",
                table: "GroupMessages");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "GroupMessages");

            migrationBuilder.DropColumn(
                name: "IsMuted",
                table: "GroupMembers");

            migrationBuilder.DropColumn(
                name: "LeftAt",
                table: "GroupMembers");

            migrationBuilder.AlterColumn<int>(
                name: "SenderId",
                table: "GroupMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMessages_Users_SenderId",
                table: "GroupMessages",
                column: "SenderId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}