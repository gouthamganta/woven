using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WovenBackend.Migrations
{
    public partial class AddMatchSignalLog : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchSignalLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ViewerId = table.Column<int>(type: "integer", nullable: false),
                    CandidateId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    EventValue = table.Column<float>(type: "real", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSignalLogs", x => x.Id);
                    table.CheckConstraint("ck_match_signal_log_no_self", "\"viewer_id\" <> \"candidate_id\"");
                    table.ForeignKey(
                        name: "FK_MatchSignalLogs_Users_ViewerId",
                        column: x => x.ViewerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchSignalLogs_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "ix_match_signal_log_pair_time",
                table: "MatchSignalLogs",
                columns: new[] { "ViewerId", "CandidateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ix_match_signal_log_viewer_event",
                table: "MatchSignalLogs",
                columns: new[] { "ViewerId", "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ix_match_signal_log_occurred_at",
                table: "MatchSignalLogs",
                column: "OccurredAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MatchSignalLogs");
        }
    }
}
