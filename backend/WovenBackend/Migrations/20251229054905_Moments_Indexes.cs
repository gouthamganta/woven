using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class Moments_Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_matches_user_a_id",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_user_b_id",
                table: "matches");

            migrationBuilder.CreateIndex(
                name: "IX_pending_matches_user_id_created_at",
                table: "pending_matches",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_matches_balloon_state_expires_at",
                table: "matches",
                columns: new[] { "balloon_state", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_matches_user_a_id_balloon_state_expires_at",
                table: "matches",
                columns: new[] { "user_a_id", "balloon_state", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_matches_user_a_id_user_b_id_balloon_state",
                table: "matches",
                columns: new[] { "user_a_id", "user_b_id", "balloon_state" },
                unique: true,
                filter: "\"balloon_state\" = 'ACTIVE'");

            migrationBuilder.CreateIndex(
                name: "IX_matches_user_b_id_balloon_state_expires_at",
                table: "matches",
                columns: new[] { "user_b_id", "balloon_state", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_blocks_blocker_id",
                table: "blocks",
                column: "blocker_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pending_matches_user_id_created_at",
                table: "pending_matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_balloon_state_expires_at",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_user_a_id_balloon_state_expires_at",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_user_a_id_user_b_id_balloon_state",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_user_b_id_balloon_state_expires_at",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_blocks_blocker_id",
                table: "blocks");

            migrationBuilder.CreateIndex(
                name: "IX_matches_user_a_id",
                table: "matches",
                column: "user_a_id");

            migrationBuilder.CreateIndex(
                name: "IX_matches_user_b_id",
                table: "matches",
                column: "user_b_id");
        }
    }
}
