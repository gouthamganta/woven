using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddGamesSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "games_initiated",
                table: "daily_interactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "games_played",
                table: "daily_interactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "game_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    initiator_user_id = table.Column<int>(type: "integer", nullable: false),
                    round_data = table.Column<string>(type: "jsonb", nullable: false),
                    current_round = table.Column<int>(type: "integer", nullable: false),
                    total_rounds = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_sessions_Users_initiator_user_id",
                        column: x => x.initiator_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_game_sessions_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_analytics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    completed = table.Column<bool>(type: "boolean", nullable: false),
                    messages_before_game = table.Column<int>(type: "integer", nullable: false),
                    messages_1h_after = table.Column<int>(type: "integer", nullable: true),
                    messages_24h_after = table.Column<int>(type: "integer", nullable: true),
                    match_still_active = table.Column<bool>(type: "boolean", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_analytics", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_analytics_game_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_a_score = table.Column<int>(type: "integer", nullable: true),
                    user_b_score = table.Column<int>(type: "integer", nullable: true),
                    winner_user_id = table.Column<int>(type: "integer", nullable: true),
                    result_data = table.Column<string>(type: "jsonb", nullable: false),
                    ai_insight = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_results_game_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_game_results_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_rounds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_number = table.Column<int>(type: "integer", nullable: false),
                    guesser_user_id = table.Column<int>(type: "integer", nullable: false),
                    target_user_id = table.Column<int>(type: "integer", nullable: false),
                    questions_json = table.Column<string>(type: "jsonb", nullable: false),
                    answers_json = table.Column<string>(type: "jsonb", nullable: true),
                    target_answers_json = table.Column<string>(type: "jsonb", nullable: true),
                    score = table.Column<int>(type: "integer", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_rounds", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_rounds_Users_guesser_user_id",
                        column: x => x.guesser_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_game_rounds_Users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_game_rounds_game_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_daily_games_cap",
                table: "daily_interactions",
                sql: "\"games_initiated\" >= 0 AND \"games_initiated\" <= 2");

            migrationBuilder.CreateIndex(
                name: "IX_game_analytics_game_type_completed",
                table: "game_analytics",
                columns: new[] { "game_type", "completed" });

            migrationBuilder.CreateIndex(
                name: "IX_game_analytics_session_id",
                table: "game_analytics",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_results_match_id_created_at",
                table: "game_results",
                columns: new[] { "match_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_game_results_session_id",
                table: "game_results",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_rounds_guesser_user_id",
                table: "game_rounds",
                column: "guesser_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_rounds_session_id_round_number",
                table: "game_rounds",
                columns: new[] { "session_id", "round_number" });

            migrationBuilder.CreateIndex(
                name: "IX_game_rounds_target_user_id",
                table: "game_rounds",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_sessions_expires_at_status",
                table: "game_sessions",
                columns: new[] { "expires_at", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_game_sessions_initiator_user_id",
                table: "game_sessions",
                column: "initiator_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_sessions_match_id_status",
                table: "game_sessions",
                columns: new[] { "match_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_analytics");

            migrationBuilder.DropTable(
                name: "game_results");

            migrationBuilder.DropTable(
                name: "game_rounds");

            migrationBuilder.DropTable(
                name: "game_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_daily_games_cap",
                table: "daily_interactions");

            migrationBuilder.DropColumn(
                name: "games_initiated",
                table: "daily_interactions");

            migrationBuilder.DropColumn(
                name: "games_played",
                table: "daily_interactions");
        }
    }
}
