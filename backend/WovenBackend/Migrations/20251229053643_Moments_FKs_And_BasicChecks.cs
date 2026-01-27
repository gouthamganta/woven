using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class Moments_FKs_And_BasicChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blocks",
                columns: table => new
                {
                    blocker_id = table.Column<int>(type: "integer", nullable: false),
                    blocked_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocks", x => new { x.blocker_id, x.blocked_id });
                    table.CheckConstraint("ck_blocks_no_self", "\"blocker_id\" <> \"blocked_id\"");
                    table.ForeignKey(
                        name: "FK_blocks_Users_blocked_id",
                        column: x => x.blocked_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_blocks_Users_blocker_id",
                        column: x => x.blocker_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "daily_interactions",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    date_utc = table.Column<DateOnly>(type: "date", nullable: false),
                    total_used = table.Column<short>(type: "smallint", nullable: false),
                    pending_used = table.Column<short>(type: "smallint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_interactions", x => new { x.user_id, x.date_utc });
                    table.ForeignKey(
                        name: "FK_daily_interactions_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_a_id = table.Column<int>(type: "integer", nullable: false),
                    user_b_id = table.Column<int>(type: "integer", nullable: false),
                    match_type = table.Column<int>(type: "integer", nullable: false),
                    edge_owner_id = table.Column<int>(type: "integer", nullable: true),
                    balloon_state = table.Column<int>(type: "integer", nullable: false),
                    closed_reason = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches", x => x.id);
                    table.CheckConstraint("ck_matches_no_self", "\"user_a_id\" <> \"user_b_id\"");
                    table.ForeignKey(
                        name: "FK_matches_Users_edge_owner_id",
                        column: x => x.edge_owner_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_matches_Users_user_a_id",
                        column: x => x.user_a_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_matches_Users_user_b_id",
                        column: x => x.user_b_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pending_matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    target_user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_matches", x => x.id);
                    table.CheckConstraint("ck_pending_no_self", "\"user_id\" <> \"target_user_id\"");
                    table.ForeignKey(
                        name: "FK_pending_matches_Users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pending_matches_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blocks_blocked_id",
                table: "blocks",
                column: "blocked_id");

            migrationBuilder.CreateIndex(
                name: "IX_matches_edge_owner_id",
                table: "matches",
                column: "edge_owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_matches_user_a_id",
                table: "matches",
                column: "user_a_id");

            migrationBuilder.CreateIndex(
                name: "IX_matches_user_b_id",
                table: "matches",
                column: "user_b_id");

            migrationBuilder.CreateIndex(
                name: "IX_pending_matches_target_user_id",
                table: "pending_matches",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_pending_matches_user_id",
                table: "pending_matches",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blocks");

            migrationBuilder.DropTable(
                name: "daily_interactions");

            migrationBuilder.DropTable(
                name: "matches");

            migrationBuilder.DropTable(
                name: "pending_matches");
        }
    }
}
