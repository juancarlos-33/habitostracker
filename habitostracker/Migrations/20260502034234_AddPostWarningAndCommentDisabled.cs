using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace habitostracker.Migrations
{
    /// <inheritdoc />
    public partial class AddPostWarningAndCommentDisabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Agregar columna IsUnderWarning (booleano)
            migrationBuilder.AddColumn<bool>(
                name: "IsUnderWarning",
                table: "Posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Agregar columna CommentsDisabled (booleano)
            migrationBuilder.AddColumn<bool>(
                name: "CommentsDisabled",
                table: "Posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsUnderWarning",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "CommentsDisabled",
                table: "Posts");
        }
    }
}