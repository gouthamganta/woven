using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddFoundationalQuestionSetFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GenerationMetaJson",
                table: "UserFoundationalQuestionSets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QuestionsHash",
                table: "UserFoundationalQuestionSets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionsSource",
                table: "UserFoundationalQuestionSets",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GenerationMetaJson",
                table: "UserFoundationalQuestionSets");

            migrationBuilder.DropColumn(
                name: "QuestionsHash",
                table: "UserFoundationalQuestionSets");

            migrationBuilder.DropColumn(
                name: "QuestionsSource",
                table: "UserFoundationalQuestionSets");
        }
    }
}
