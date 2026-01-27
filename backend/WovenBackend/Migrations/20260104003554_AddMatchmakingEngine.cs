using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchmakingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RelationshipStructure",
                table: "UserPreferences",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DailyDecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    DateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ItemsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyDecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyDecks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "match_outcomes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    candidate_id = table.Column<int>(type: "integer", nullable: false),
                    date_utc = table.Column<DateOnly>(type: "date", nullable: false),
                    chat_started = table.Column<bool>(type: "boolean", nullable: false),
                    messages_24h = table.Column<int>(type: "integer", nullable: false),
                    expired = table.Column<bool>(type: "boolean", nullable: false),
                    unmatched = table.Column<bool>(type: "boolean", nullable: false),
                    blocked = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_outcomes", x => x.id);
                    table.ForeignKey(
                        name: "FK_match_outcomes_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchExplanations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CandidateId = table.Column<int>(type: "integer", nullable: false),
                    DateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    Headline = table.Column<string>(type: "text", nullable: false),
                    BulletsJson = table.Column<string>(type: "text", nullable: false),
                    Tone = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchExplanations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchExplanations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserVectors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    VectorJson = table.Column<string>(type: "text", nullable: false),
                    PillarScoresJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVectors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserVectors_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserVectorTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TagType = table.Column<string>(type: "text", nullable: false),
                    Tag = table.Column<string>(type: "text", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVectorTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserVectorTags_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyDecks_UserId_DateUtc",
                table: "DailyDecks",
                columns: new[] { "UserId", "DateUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_match_outcomes_match_id",
                table: "match_outcomes",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "IX_match_outcomes_user_id_candidate_id_date_utc",
                table: "match_outcomes",
                columns: new[] { "user_id", "candidate_id", "date_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchExplanations_UserId_CandidateId_DateUtc",
                table: "MatchExplanations",
                columns: new[] { "UserId", "CandidateId", "DateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserVectors_UserId_Version",
                table: "UserVectors",
                columns: new[] { "UserId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserVectorTags_Tag_TagType",
                table: "UserVectorTags",
                columns: new[] { "Tag", "TagType" });

            migrationBuilder.CreateIndex(
                name: "IX_UserVectorTags_UserId_Version_TagType",
                table: "UserVectorTags",
                columns: new[] { "UserId", "Version", "TagType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyDecks");

            migrationBuilder.DropTable(
                name: "match_outcomes");

            migrationBuilder.DropTable(
                name: "MatchExplanations");

            migrationBuilder.DropTable(
                name: "UserVectors");

            migrationBuilder.DropTable(
                name: "UserVectorTags");

            migrationBuilder.DropColumn(
                name: "RelationshipStructure",
                table: "UserPreferences");
        }
    }
}
