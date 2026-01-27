using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddAnswersAndSignalsToFoundationalQuestionSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnswersJson",
                table: "UserFoundationalQuestionSets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SignalsJson",
                table: "UserFoundationalQuestionSets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "UserFoundationalQuestionSets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswersJson",
                table: "UserFoundationalQuestionSets");

            migrationBuilder.DropColumn(
                name: "SignalsJson",
                table: "UserFoundationalQuestionSets");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "UserFoundationalQuestionSets");
        }
    }
}
