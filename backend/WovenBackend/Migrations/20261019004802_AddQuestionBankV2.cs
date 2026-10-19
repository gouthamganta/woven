using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionBankV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FoundationalQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Pillar = table.Column<string>(type: "text", nullable: false),
                    GameType = table.Column<string>(type: "text", nullable: false),
                    QuestionData = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    SourceInstrument = table.Column<string>(type: "text", nullable: false),
                    Citation = table.Column<string>(type: "text", nullable: false),
                    OriginalItem = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoundationalQuestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserGameResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Pillar = table.Column<string>(type: "text", nullable: false),
                    GameType = table.Column<string>(type: "text", nullable: false),
                    AnswerJson = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    LlmInterpretedScore = table.Column<decimal>(type: "numeric", nullable: true),
                    LlmInterpretation = table.Column<string>(type: "text", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGameResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserQuestionHistory",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShownAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserQuestionHistory", x => new { x.UserId, x.QuestionId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FoundationalQuestions");

            migrationBuilder.DropTable(
                name: "UserGameResponses");

            migrationBuilder.DropTable(
                name: "UserQuestionHistory");
        }
    }
}
