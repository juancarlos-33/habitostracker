using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace habitostracker.Migrations
{
    public partial class AddHabitProgress : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HabitProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HabitId = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    StreakDays = table.Column<int>(type: "integer", nullable: false),
                    CompletionRate = table.Column<int>(type: "integer", nullable: false),
                    SharedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HabitProgresses_Habits_HabitId",
                        column: x => x.HabitId,
                        principalTable: "Habits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HabitProgresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HabitProgressComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HabitProgressId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitProgressComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HabitProgressComments_HabitProgresses_HabitProgressId",
                        column: x => x.HabitProgressId,
                        principalTable: "HabitProgresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HabitProgressComments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HabitProgressReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HabitProgressId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Emoji = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitProgressReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HabitProgressReactions_HabitProgresses_HabitProgressId",
                        column: x => x.HabitProgressId,
                        principalTable: "HabitProgresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HabitProgressReactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_HabitProgressComments_HabitProgressId", table: "HabitProgressComments", column: "HabitProgressId");
            migrationBuilder.CreateIndex(name: "IX_HabitProgressComments_UserId", table: "HabitProgressComments", column: "UserId");
            migrationBuilder.CreateIndex(name: "IX_HabitProgresses_HabitId", table: "HabitProgresses", column: "HabitId");
            migrationBuilder.CreateIndex(name: "IX_HabitProgresses_UserId", table: "HabitProgresses", column: "UserId");
            migrationBuilder.CreateIndex(name: "IX_HabitProgressReactions_HabitProgressId", table: "HabitProgressReactions", column: "HabitProgressId");
            migrationBuilder.CreateIndex(name: "IX_HabitProgressReactions_UserId", table: "HabitProgressReactions", column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "HabitProgressComments");
            migrationBuilder.DropTable(name: "HabitProgressReactions");
            migrationBuilder.DropTable(name: "HabitProgresses");
        }
    }
}