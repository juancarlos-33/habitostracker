using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace habitostracker.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityAnswer1",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityAnswer2",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityAnswer3",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityQuestion1",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityQuestion2",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityQuestion3",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityAnswer1",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityAnswer2",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityAnswer3",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityQuestion1",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityQuestion2",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityQuestion3",
                table: "Users");
        }
    }
}