using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class Moments_Invariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pending_matches_user_id",
                table: "pending_matches");

            migrationBuilder.AlterColumn<string>(
                name: "match_type",
                table: "matches",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "closed_reason",
                table: "matches",
                type: "text",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "balloon_state",
                table: "matches",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateIndex(
                name: "IX_pending_matches_user_id_target_user_id",
                table: "pending_matches",
                columns: new[] { "user_id", "target_user_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_matches_expires_after_created",
                table: "matches",
                sql: "\"expires_at\" > \"created_at\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_matches_state_closed_fields",
                table: "matches",
                sql: "\r\n                (\r\n                  \"balloon_state\" = 'ACTIVE' AND\r\n                  \"closed_reason\" IS NULL AND\r\n                  \"closed_at\" IS NULL\r\n                )\r\n                OR\r\n                (\r\n                  \"balloon_state\" = 'CLOSED' AND\r\n                  \"closed_reason\" IS NOT NULL AND\r\n                  \"closed_at\" IS NOT NULL\r\n                )\r\n                ");

            migrationBuilder.AddCheckConstraint(
                name: "ck_matches_type_edge_owner",
                table: "matches",
                sql: "\r\n                (\r\n                  \"match_type\" = 'PURE' AND\r\n                  \"edge_owner_id\" IS NULL\r\n                )\r\n                OR\r\n                (\r\n                  \"match_type\" = 'EDGE' AND\r\n                  \"edge_owner_id\" IS NOT NULL\r\n                )\r\n                ");

            migrationBuilder.AddCheckConstraint(
                name: "ck_daily_pending_cap",
                table: "daily_interactions",
                sql: "\"pending_used\" >= 0 AND \"pending_used\" <= 2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_daily_pending_le_total",
                table: "daily_interactions",
                sql: "\"pending_used\" <= \"total_used\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_daily_total_cap",
                table: "daily_interactions",
                sql: "\"total_used\" >= 0 AND \"total_used\" <= 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pending_matches_user_id_target_user_id",
                table: "pending_matches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_matches_expires_after_created",
                table: "matches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_matches_state_closed_fields",
                table: "matches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_matches_type_edge_owner",
                table: "matches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_daily_pending_cap",
                table: "daily_interactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_daily_pending_le_total",
                table: "daily_interactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_daily_total_cap",
                table: "daily_interactions");

            migrationBuilder.AlterColumn<int>(
                name: "match_type",
                table: "matches",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<int>(
                name: "closed_reason",
                table: "matches",
                type: "integer",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "balloon_state",
                table: "matches",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_pending_matches_user_id",
                table: "pending_matches",
                column: "user_id");
        }
    }
}
