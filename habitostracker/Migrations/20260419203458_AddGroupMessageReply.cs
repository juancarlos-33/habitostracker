using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace habitostracker.Migrations
{
    public partial class AddGroupMessageReply : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReplyToMessageId",
                table: "GroupMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupMessages_ReplyToMessageId",
                table: "GroupMessages",
                column: "ReplyToMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMessages_GroupMessages_ReplyToMessageId",
                table: "GroupMessages",
                column: "ReplyToMessageId",
                principalTable: "GroupMessages",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupMessages_GroupMessages_ReplyToMessageId",
                table: "GroupMessages");

            migrationBuilder.DropIndex(
                name: "IX_GroupMessages_ReplyToMessageId",
                table: "GroupMessages");

            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                table: "GroupMessages");
        }
    }
}