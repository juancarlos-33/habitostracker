using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace habitostracker.Migrations
{
    /// <inheritdoc />
    public partial class AddPinnedMessageToGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupMessages_GroupMessages_ReplyToMessageId",
                table: "GroupMessages");

            migrationBuilder.AddColumn<int>(
                name: "PinnedMessageId",
                table: "Groups",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_PinnedMessageId",
                table: "Groups",
                column: "PinnedMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMessages_GroupMessages_ReplyToMessageId",
                table: "GroupMessages",
                column: "ReplyToMessageId",
                principalTable: "GroupMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_GroupMessages_PinnedMessageId",
                table: "Groups",
                column: "PinnedMessageId",
                principalTable: "GroupMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupMessages_GroupMessages_ReplyToMessageId",
                table: "GroupMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_Groups_GroupMessages_PinnedMessageId",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_PinnedMessageId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "PinnedMessageId",
                table: "Groups");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMessages_GroupMessages_ReplyToMessageId",
                table: "GroupMessages",
                column: "ReplyToMessageId",
                principalTable: "GroupMessages",
                principalColumn: "Id");
        }
    }
}