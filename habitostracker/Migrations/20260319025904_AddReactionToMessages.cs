using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace habitostracker.Migrations
{
    /// <inheritdoc />
    public partial class AddReactionToMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reaction",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reaction",
                table: "Messages");
        }
    }
}
