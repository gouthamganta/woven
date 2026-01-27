using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFoundationalQuestionSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserOptionalFields_UserId_Key",
                table: "UserOptionalFields");

            migrationBuilder.AlterColumn<string>(
                name: "ProfileStatus",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "AgeMax",
                table: "UserPreferences",
                type: "integer",
                nullable: false,
                defaultValue: 99);

            migrationBuilder.AddColumn<int>(
                name: "AgeMin",
                table: "UserPreferences",
                type: "integer",
                nullable: false,
                defaultValue: 18);

            migrationBuilder.AlterColumn<string>(
                name: "Visibility",
                table: "UserOptionalFields",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "UserFoundationalQuestionSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    QuestionsJson = table.Column<string>(type: "text", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFoundationalQuestionSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFoundationalQuestionSets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserWeeklyVibes_ExpiresAt",
                table: "UserWeeklyVibes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserOptionalFields_UserId_Key",
                table: "UserOptionalFields",
                columns: new[] { "UserId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserFoundationalQuestionSets_UserId",
                table: "UserFoundationalQuestionSets",
                column: "UserId",
                unique: true,
                filter: "\"AnsweredAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserFoundationalQuestionSets_UserId_Version",
                table: "UserFoundationalQuestionSets",
                columns: new[] { "UserId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserFoundationalQuestionSets");

            migrationBuilder.DropIndex(
                name: "IX_UserWeeklyVibes_ExpiresAt",
                table: "UserWeeklyVibes");

            migrationBuilder.DropIndex(
                name: "IX_UserOptionalFields_UserId_Key",
                table: "UserOptionalFields");

            migrationBuilder.DropColumn(
                name: "AgeMax",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "AgeMin",
                table: "UserPreferences");

            migrationBuilder.AlterColumn<int>(
                name: "ProfileStatus",
                table: "Users",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<int>(
                name: "Visibility",
                table: "UserOptionalFields",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_UserOptionalFields_UserId_Key",
                table: "UserOptionalFields",
                columns: new[] { "UserId", "Key" });
        }
    }
}
