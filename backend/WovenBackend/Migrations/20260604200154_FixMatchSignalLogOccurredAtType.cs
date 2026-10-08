using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class FixMatchSignalLogOccurredAtType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The scaffolded step here was an AlterColumn (drop the false default) that assumed
            // "Users"."CoachingOptedOut" already existed. That column is only added by the manual
            // 20260604000001_AddCoachingSummaries migration, which has no [Migration]/[DbContext]
            // metadata, so EF never discovers or applies it. On a fresh database the column is
            // missing and the AlterColumn fails; on databases where it was added out-of-band it
            // exists. We deliberately don't register the manual migration, since it would create a
            // duplicate snake_case coaching_summaries table alongside the one created below.
            //
            // ADD COLUMN IF NOT EXISTS is a no-op when the column exists, so existing values and
            // type are kept. When it's missing, the false default backfills existing rows to
            // satisfy NOT NULL. Dropping the default afterwards gives the same end state as the
            // original AlterColumn: boolean NOT NULL, no database default.
            migrationBuilder.Sql("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "CoachingOptedOut" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "Users" ALTER COLUMN "CoachingOptedOut" DROP DEFAULT;
                """);

            migrationBuilder.CreateTable(
                name: "coaching_summaries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SummaryText = table.Column<string>(type: "text", nullable: false),
                    InterpretedNarrative = table.Column<string>(type: "text", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DismissedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OptedOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coaching_summaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_coaching_summaries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageLoveReactions_MessageId",
                table: "MessageLoveReactions",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSignalLogs_CandidateId",
                table: "MatchSignalLogs",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionScores_CandidateId",
                table: "ConnectionScores",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "ix_coaching_summaries_user_id",
                table: "coaching_summaries",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "uq_coaching_summary_user_week",
                table: "coaching_summaries",
                columns: new[] { "UserId", "WeekStartDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coaching_summaries");

            migrationBuilder.DropIndex(
                name: "IX_MessageLoveReactions_MessageId",
                table: "MessageLoveReactions");

            migrationBuilder.DropIndex(
                name: "IX_MatchSignalLogs_CandidateId",
                table: "MatchSignalLogs");

            migrationBuilder.DropIndex(
                name: "IX_ConnectionScores_CandidateId",
                table: "ConnectionScores");

            migrationBuilder.AlterColumn<bool>(
                name: "CoachingOptedOut",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");
        }
    }
}
