using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WovenBackend.Migrations
{
    public partial class AddCoachingSummaries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // coaching_summaries table
            migrationBuilder.Sql("""
                CREATE TABLE coaching_summaries (
                    id                      BIGSERIAL PRIMARY KEY,
                    user_id                 INTEGER NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
                    week_start_date         DATE NOT NULL,
                    summary_text            TEXT NOT NULL,
                    interpreted_narrative   TEXT NOT NULL,
                    delivered_at            TIMESTAMPTZ NOT NULL,
                    dismissed_at            TIMESTAMPTZ,
                    opted_out_at            TIMESTAMPTZ,
                    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    CONSTRAINT uq_coaching_summary_user_week UNIQUE (user_id, week_start_date)
                );
                """);

            migrationBuilder.Sql("""CREATE INDEX ix_coaching_summaries_user_id ON coaching_summaries (user_id, created_at DESC);""");

            // coaching_opted_out column on Users
            migrationBuilder.AddColumn<bool>(
                name: "CoachingOptedOut",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Covering index for signal aggregation queries (verify with EXPLAIN ANALYZE before enabling in prod)
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_match_signal_logs_user_week
                    ON "MatchSignalLogs" ("ViewerId", "OccurredAt" DESC)
                    INCLUDE ("EventType");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS ix_match_signal_logs_user_week;""");
            migrationBuilder.DropColumn(name: "CoachingOptedOut", table: "Users");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS coaching_summaries;""");
        }
    }
}
