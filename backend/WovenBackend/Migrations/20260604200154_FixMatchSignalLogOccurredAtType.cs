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

            // Similarly, the coaching_summaries table may already exist from the manual migration.
            // CREATE TABLE IF NOT EXISTS handles both fresh installs and existing databases.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS coaching_summaries (
                    "Id" bigserial PRIMARY KEY,
                    "UserId" integer NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
                    "WeekStartDate" date NOT NULL,
                    "SummaryText" text NOT NULL,
                    "InterpretedNarrative" text NOT NULL,
                    "DeliveredAt" timestamptz NOT NULL,
                    "DismissedAt" timestamptz,
                    "OptedOutAt" timestamptz,
                    "CreatedAt" timestamptz NOT NULL
                );
                """);

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

            // Create coaching_summaries indexes if they don't already exist
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_coaching_summaries_user_id
                    ON coaching_summaries ("UserId", "CreatedAt");
                CREATE UNIQUE INDEX IF NOT EXISTS uq_coaching_summary_user_week
                    ON coaching_summaries ("UserId", "WeekStartDate");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop coaching_summaries and its indexes if they exist
            migrationBuilder.Sql("DROP TABLE IF EXISTS coaching_summaries;");

            migrationBuilder.DropIndex(
                name: "IX_MessageLoveReactions_MessageId",
                table: "MessageLoveReactions");

            migrationBuilder.DropIndex(
                name: "IX_MatchSignalLogs_CandidateId",
                table: "MatchSignalLogs");

            migrationBuilder.DropIndex(
                name: "IX_ConnectionScores_CandidateId",
                table: "ConnectionScores");

            // Restore CoachingOptedOut default or drop if it was added by this migration
            migrationBuilder.Sql("""
                ALTER TABLE "Users" ALTER COLUMN "CoachingOptedOut" SET DEFAULT FALSE;
                """);
        }
    }
}
