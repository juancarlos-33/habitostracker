using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace habitostracker.Migrations
{
    /// <inheritdoc />
    public partial class AddRelationsToPostReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PostReports_PostId",
                table: "PostReports",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_PostReports_ReportedByUserId",
                table: "PostReports",
                column: "ReportedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PostReports_Posts_PostId",
                table: "PostReports",
                column: "PostId",
                principalTable: "Posts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PostReports_Users_ReportedByUserId",
                table: "PostReports",
                column: "ReportedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostReports_Posts_PostId",
                table: "PostReports");

            migrationBuilder.DropForeignKey(
                name: "FK_PostReports_Users_ReportedByUserId",
                table: "PostReports");

            migrationBuilder.DropIndex(
                name: "IX_PostReports_PostId",
                table: "PostReports");

            migrationBuilder.DropIndex(
                name: "IX_PostReports_ReportedByUserId",
                table: "PostReports");
        }
    }
}
