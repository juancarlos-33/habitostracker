using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace habitostracker.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLastIp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastIp",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastIp",
                table: "Users");
        }
    }
}
