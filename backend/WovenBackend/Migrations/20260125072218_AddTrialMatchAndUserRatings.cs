using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialMatchAndUserRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_trial",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_ends_at",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_started_at",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_a_decision",
                table: "matches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_b_decision",
                table: "matches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_ratings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rated_user_id = table.Column<int>(type: "integer", nullable: false),
                    rater_user_id = table.Column<int>(type: "integer", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rating_value = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_ratings", x => x.id);
                    table.CheckConstraint("ck_rating_value_range", "\"rating_value\" >= -100 AND \"rating_value\" <= 100");
                    table.ForeignKey(
                        name: "FK_user_ratings_Users_rated_user_id",
                        column: x => x.rated_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_ratings_Users_rater_user_id",
                        column: x => x.rater_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_ratings_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_ratings_match_id",
                table: "user_ratings",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_ratings_rated_user_id",
                table: "user_ratings",
                column: "rated_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_ratings_rated_user_id_rater_user_id_match_id",
                table: "user_ratings",
                columns: new[] { "rated_user_id", "rater_user_id", "match_id" });

            migrationBuilder.CreateIndex(
                name: "IX_user_ratings_rater_user_id",
                table: "user_ratings",
                column: "rater_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_ratings");

            migrationBuilder.DropColumn(
                name: "is_trial",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "trial_ends_at",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "trial_started_at",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "user_a_decision",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "user_b_decision",
                table: "matches");
        }
    }
}
