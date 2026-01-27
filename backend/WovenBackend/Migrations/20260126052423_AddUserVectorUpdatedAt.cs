using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddUserVectorUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "UserVectors",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "metadata_json",
                table: "game_sessions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "game_outcomes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    initiator_user_id = table.Column<int>(type: "integer", nullable: false),
                    partner_user_id = table.Column<int>(type: "integer", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    difficulty = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bucket = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    intent_alignment = table.Column<double>(type: "double precision", nullable: false),
                    total_rounds = table.Column<int>(type: "integer", nullable: false),
                    completed_rounds = table.Column<int>(type: "integer", nullable: false),
                    initiator_score = table.Column<int>(type: "integer", nullable: false),
                    partner_score = table.Column<int>(type: "integer", nullable: false),
                    average_response_time_ms = table.Column<double>(type: "double precision", nullable: false),
                    completion_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    user_feedback = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_outcomes", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_outcomes_Users_initiator_user_id",
                        column: x => x.initiator_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_game_outcomes_Users_partner_user_id",
                        column: x => x.partner_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_game_outcomes_game_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_game_outcomes_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_outcomes_initiator_user_id",
                table: "game_outcomes",
                column: "initiator_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_outcomes_initiator_user_id_created_at",
                table: "game_outcomes",
                columns: new[] { "initiator_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_game_outcomes_match_id",
                table: "game_outcomes",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_outcomes_partner_user_id",
                table: "game_outcomes",
                column: "partner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_outcomes_partner_user_id_created_at",
                table: "game_outcomes",
                columns: new[] { "partner_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_game_outcomes_session_id",
                table: "game_outcomes",
                column: "session_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_outcomes");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "UserVectors");

            migrationBuilder.DropColumn(
                name: "metadata_json",
                table: "game_sessions");
        }
    }
}
