using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace habitostracker.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Browser",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Device",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatingSystem",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Browser",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Device",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OperatingSystem",
                table: "Users");
        }
    }
}
