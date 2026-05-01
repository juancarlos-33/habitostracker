using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace habitostracker.Migrations
{
    /// <inheritdoc />
    public partial class AddFeaturesForAnnouncementsAndSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ========== COLUMNAS PARA TABLA Users ==========
            migrationBuilder.AddColumn<bool>(
                name: "HasWelcomePost",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RiskLevel",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastWarningAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarningMessage",
                table: "Users",
                type: "text",
                nullable: true);

            // ========== COLUMNAS PARA TABLA Posts ==========
            migrationBuilder.AddColumn<bool>(
                name: "CommentsDisabled",
                table: "Posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOfficialAnnouncement",
                table: "Posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsWarningPost",
                table: "Posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsWelcomePost",
                table: "Posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ========== COLUMNAS PARA TABLA Groups ==========
            migrationBuilder.AddColumn<bool>(
                name: "ShowHealthWarning",
                table: "Groups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "HealthWarningMessage",
                table: "Groups",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ========== ELIMINAR COLUMNAS DE Users ==========
            migrationBuilder.DropColumn(
                name: "HasWelcomePost",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastWarningAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WarningMessage",
                table: "Users");

            // ========== ELIMINAR COLUMNAS DE Posts ==========
            migrationBuilder.DropColumn(
                name: "CommentsDisabled",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "IsOfficialAnnouncement",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "IsWarningPost",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "IsWelcomePost",
                table: "Posts");

            // ========== ELIMINAR COLUMNAS DE Groups ==========
            migrationBuilder.DropColumn(
                name: "ShowHealthWarning",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "HealthWarningMessage",
                table: "Groups");
        }
    }
}