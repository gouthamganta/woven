using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class MomentResponsePassAndSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VerificationType",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_pronouns",
                table: "UserProfiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "high_contrast",
                table: "UserPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "reduce_motion",
                table: "UserPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "moment_responses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "time_on_card_ms",
                table: "moment_responses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AvgResponseTimeMs",
                table: "chat_threads",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastMessageAt",
                table: "chat_threads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MessageCount",
                table: "chat_threads",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ab_experiments",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    variants = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ab_experiments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analytics_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    properties = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analytics_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_availability_signals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SignalText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_availability_signals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_availability_signals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chat_availability_signals_chat_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "chat_threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "date_feedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MetInPerson = table.Column<bool>(type: "boolean", nullable: false),
                    Stars = table.Column<int>(type: "integer", nullable: true),
                    FeltRightText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FeltOffText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MeetAgain = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_date_feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_date_feedback_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_date_feedback_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "date_feedback_prompts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RescheduleCount = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_date_feedback_prompts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_date_feedback_prompts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_date_feedback_prompts_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reference_photo_embeddings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    embedding = table.Column<Vector>(type: "vector(512)", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_photo_embeddings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_verifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_verifications_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ab_assignments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    experiment_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    variant = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ab_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_ab_assignments_ab_experiments_experiment_id",
                        column: x => x.experiment_id,
                        principalTable: "ab_experiments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ab_conversions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    experiment_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    conversion_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ab_conversions", x => x.id);
                    table.ForeignKey(
                        name: "FK_ab_conversions_ab_experiments_experiment_id",
                        column: x => x.experiment_id,
                        principalTable: "ab_experiments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ab_assignments_experiment_id",
                table: "ab_assignments",
                column: "experiment_id");

            migrationBuilder.CreateIndex(
                name: "ix_ab_assignments_user_id",
                table: "ab_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_ab_assignments_user_experiment",
                table: "ab_assignments",
                columns: new[] { "user_id", "experiment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ab_conversions_experiment_id",
                table: "ab_conversions",
                column: "experiment_id");

            migrationBuilder.CreateIndex(
                name: "ix_ab_conversions_user_experiment",
                table: "ab_conversions",
                columns: new[] { "user_id", "experiment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_created_at",
                table: "analytics_events",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_event_type",
                table: "analytics_events",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_user_id_hash",
                table: "analytics_events",
                column: "user_id_hash",
                filter: "\"user_id_hash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_chat_avail_thread",
                table: "chat_availability_signals",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_availability_signals_UserId",
                table: "chat_availability_signals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_date_feedback_UserId",
                table: "date_feedback",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "uq_date_feedback_match_user",
                table: "date_feedback",
                columns: new[] { "MatchId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_date_fb_prompts_scheduled",
                table: "date_feedback_prompts",
                columns: new[] { "ScheduledFor", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_date_feedback_prompts_UserId",
                table: "date_feedback_prompts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "uq_date_fb_prompt_match_user",
                table: "date_feedback_prompts",
                columns: new[] { "MatchId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_verifications_user_status",
                table: "user_verifications",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ab_assignments");

            migrationBuilder.DropTable(
                name: "ab_conversions");

            migrationBuilder.DropTable(
                name: "analytics_events");

            migrationBuilder.DropTable(
                name: "chat_availability_signals");

            migrationBuilder.DropTable(
                name: "date_feedback");

            migrationBuilder.DropTable(
                name: "date_feedback_prompts");

            migrationBuilder.DropTable(
                name: "reference_photo_embeddings");

            migrationBuilder.DropTable(
                name: "user_verifications");

            migrationBuilder.DropTable(
                name: "ab_experiments");

            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerificationType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "display_pronouns",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "high_contrast",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "reduce_motion",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "source",
                table: "moment_responses");

            migrationBuilder.DropColumn(
                name: "time_on_card_ms",
                table: "moment_responses");

            migrationBuilder.DropColumn(
                name: "AvgResponseTimeMs",
                table: "chat_threads");

            migrationBuilder.DropColumn(
                name: "LastMessageAt",
                table: "chat_threads");

            migrationBuilder.DropColumn(
                name: "MessageCount",
                table: "chat_threads");
        }
    }
}
