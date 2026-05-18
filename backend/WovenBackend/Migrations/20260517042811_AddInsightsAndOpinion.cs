using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddInsightsAndOpinion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_highlights_tiles",
                table: "highlights");

            migrationBuilder.DropForeignKey(
                name: "fk_highlights_users",
                table: "highlights");

            migrationBuilder.DropForeignKey(
                name: "fk_tiles_users",
                table: "tiles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_tiles",
                table: "tiles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_highlights",
                table: "highlights");

            migrationBuilder.RenameIndex(
                name: "uq_highlights_user_slot",
                table: "highlights",
                newName: "IX_highlights_user_id_slot_number");

            migrationBuilder.AlterColumn<Vector>(
                name: "PillarEmbedding",
                table: "UserVectors",
                type: "vector(1536)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(8)",
                oldNullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "AttachmentProxyEmbedding",
                table: "UserVectors",
                type: "vector(4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BehavioralLifestyleJson",
                table: "UserVectors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "EmotionalRhythmEmbedding",
                table: "UserVectors",
                type: "vector(48)",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "HumorEmbedding",
                table: "UserVectors",
                type: "vector(64)",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "IntentEmbedding",
                table: "UserVectors",
                type: "vector(1536)",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "LifestyleEmbedding",
                table: "UserVectors",
                type: "vector(128)",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "StyleEmbedding",
                table: "UserVectors",
                type: "vector(128)",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "GhostScore",
                table: "Users",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActiveAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "TrustScore",
                table: "Users",
                type: "real",
                nullable: false,
                defaultValue: 0.5f);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrustUpdatedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "voice_embedding",
                table: "tiles",
                type: "vector(192)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DateAgreedAt",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DateIdeaInterestedA",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DateIdeaInterestedAt",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DateIdeaInterestedB",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_tiles",
                table: "tiles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_highlights",
                table: "highlights",
                column: "id");

            migrationBuilder.CreateTable(
                name: "cf_scores",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    candidate_id = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cf_scores", x => new { x.user_id, x.candidate_id });
                    table.CheckConstraint("ck_cf_scores_no_self", "\"user_id\" <> \"candidate_id\"");
                    table.ForeignKey(
                        name: "FK_cf_scores_Users_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_cf_scores_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "friend_bridges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_a_id = table.Column<int>(type: "integer", nullable: false),
                    user_b_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friend_bridges", x => x.id);
                    table.CheckConstraint("ck_friend_bridges_status", "\"status\" IN ('pending_both','a_accepted','b_accepted','active','declined')");
                    table.ForeignKey(
                        name: "FK_friend_bridges_Users_user_a_id",
                        column: x => x.user_a_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_friend_bridges_Users_user_b_id",
                        column: x => x.user_b_id,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "moderation_queue",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewer_id = table.Column<int>(type: "integer", nullable: true),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_queue", x => x.id);
                    table.CheckConstraint("ck_moderation_decision", "decision IN ('approved','rejected')");
                    table.ForeignKey(
                        name: "fk_moderation_tile",
                        column: x => x.tile_id,
                        principalTable: "tiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_moderation_user",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orbit_gravity",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    candidate_id = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    last_orbit_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orbit_gravity", x => new { x.user_id, x.candidate_id });
                    table.ForeignKey(
                        name: "FK_orbit_gravity_Users_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_orbit_gravity_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "photo_embeddings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    photo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    embedding = table.Column<Vector>(type: "vector(512)", nullable: true),
                    embedded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photo_embeddings", x => x.id);
                    table.ForeignKey(
                        name: "FK_photo_embeddings_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    season_number = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    prompt_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "security_audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor_id = table.Column<int>(type: "integer", nullable: true),
                    ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_audit_log", x => x.id);
                    table.CheckConstraint("ck_audit_event_type", "\"event_type\" IN ('external_api_call','pii_access','encryption_key_rotation','admin_data_access','bulk_data_export','suspicious_pattern','failed_decryption')");
                    table.ForeignKey(
                        name: "FK_security_audit_log_Users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_security_audit_log_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tile_engagement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    tile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tile_engagement", x => x.id);
                    table.CheckConstraint("ck_tile_engagement_type", "\"engagement_type\" IN ('viewed','expanded','media_played','media_completed','replayed')");
                    table.ForeignKey(
                        name: "FK_tile_engagement_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tile_engagement_tiles_tile_id",
                        column: x => x.tile_id,
                        principalTable: "tiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tile_orbits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orbiter_id = table.Column<int>(type: "integer", nullable: false),
                    tile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tile_owner_id = table.Column<int>(type: "integer", nullable: false),
                    relationship_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    orbited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tile_orbits", x => x.id);
                    table.CheckConstraint("ck_tile_orbits_relationship_type", "\"relationship_type\" IN ('romantic', 'social')");
                    table.ForeignKey(
                        name: "FK_tile_orbits_Users_orbiter_id",
                        column: x => x.orbiter_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tile_orbits_Users_tile_owner_id",
                        column: x => x.tile_owner_id,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_tile_orbits_tiles_tile_id",
                        column: x => x.tile_id,
                        principalTable: "tiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tile_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tile_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_tile_reports_tile",
                        column: x => x.tile_id,
                        principalTable: "tiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tile_reports_user",
                        column: x => x.reporter_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tile_views",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    tile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tile_views", x => new { x.user_id, x.tile_id, x.viewed_at });
                    table.ForeignKey(
                        name: "FK_tile_views_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tile_views_tiles_tile_id",
                        column: x => x.tile_id,
                        principalTable: "tiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_energy_meter",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    date_utc = table.Column<DateOnly>(type: "date", nullable: false),
                    tiles_viewed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_energy_meter", x => new { x.user_id, x.date_utc });
                    table.CheckConstraint("ck_energy_tiles_nonnegative", "\"tiles_viewed\" >= 0");
                    table.ForeignKey(
                        name: "FK_user_energy_meter_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_insights",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    insights_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    opinion_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    opinion_trigger = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    opinion_submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_insights", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_insights_user",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_matching_weights",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    component = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    learned_weight = table.Column<float>(type: "real", nullable: false),
                    sample_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_matching_weights", x => new { x.user_id, x.component });
                    table.ForeignKey(
                        name: "FK_user_matching_weights_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_visual_preference",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    preference_embedding = table.Column<Vector>(type: "vector(512)", nullable: true),
                    aversion_embedding = table.Column<Vector>(type: "vector(512)", nullable: true),
                    yes_sample_count = table.Column<int>(type: "integer", nullable: false),
                    no_sample_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_visual_preference", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_user_visual_preference_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_voice_preference",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    preference_embedding = table.Column<Vector>(type: "vector(192)", nullable: true),
                    yes_sample_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_voice_preference", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_user_voice_preference_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_visual_decisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    viewer_user_id = table.Column<int>(type: "integer", nullable: false),
                    target_user_id = table.Column<int>(type: "integer", nullable: false),
                    photo_embedding_id = table.Column<int>(type: "integer", nullable: true),
                    choice = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_visual_decisions", x => x.id);
                    table.CheckConstraint("ck_visual_decision_choice", "\"choice\" IN ('YES','NO','PENDING')");
                    table.ForeignKey(
                        name: "FK_user_visual_decisions_Users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_user_visual_decisions_Users_viewer_user_id",
                        column: x => x.viewer_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_visual_decisions_photo_embeddings_photo_embedding_id",
                        column: x => x.photo_embedding_id,
                        principalTable: "photo_embeddings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_season_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    season_id = table.Column<int>(type: "integer", nullable: false),
                    pillar_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    question_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    response = table.Column<string>(type: "text", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_season_responses", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_season_responses_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_season_responses_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tiles_content_type",
                table: "tiles",
                sql: "\"content_type\" IN ('text','photo','video','voice')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tiles_expires_after_created",
                table: "tiles",
                sql: "\"expires_at\" > \"created_at\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_highlights_slot_range",
                table: "highlights",
                sql: "\"slot_number\" >= 1 AND \"slot_number\" <= 9");

            migrationBuilder.CreateIndex(
                name: "IX_cf_scores_candidate_id",
                table: "cf_scores",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_cf_scores_user_score",
                table: "cf_scores",
                columns: new[] { "user_id", "score" });

            migrationBuilder.CreateIndex(
                name: "ix_friend_bridges_user_a",
                table: "friend_bridges",
                columns: new[] { "user_a_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_friend_bridges_user_b",
                table: "friend_bridges",
                columns: new[] { "user_b_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_friend_bridges_pair",
                table: "friend_bridges",
                columns: new[] { "user_a_id", "user_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_moderation_queue_queued_at",
                table: "moderation_queue",
                column: "queued_at",
                filter: "reviewed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_queue_user_id",
                table: "moderation_queue",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_moderation_queue_tile",
                table: "moderation_queue",
                column: "tile_id",
                unique: true,
                filter: "reviewed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_orbit_gravity_candidate_id",
                table: "orbit_gravity",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_orbit_gravity_user_score",
                table: "orbit_gravity",
                columns: new[] { "user_id", "score" });

            migrationBuilder.CreateIndex(
                name: "ix_photo_embeddings_user_id",
                table: "photo_embeddings",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_seasons_season_number",
                table: "seasons",
                column: "season_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_log_actor_id",
                table: "security_audit_log",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_security_audit_log_event_type",
                table: "security_audit_log",
                columns: new[] { "event_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_security_audit_log_user_id",
                table: "security_audit_log",
                columns: new[] { "user_id", "created_at" },
                filter: "\"user_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tile_engagement_tile_id",
                table: "tile_engagement",
                column: "tile_id");

            migrationBuilder.CreateIndex(
                name: "ix_tile_engagement_user_created",
                table: "tile_engagement",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tile_orbits_orbiter_id",
                table: "tile_orbits",
                column: "orbiter_id");

            migrationBuilder.CreateIndex(
                name: "ix_tile_orbits_tile_id",
                table: "tile_orbits",
                column: "tile_id");

            migrationBuilder.CreateIndex(
                name: "ix_tile_orbits_tile_owner_id",
                table: "tile_orbits",
                column: "tile_owner_id");

            migrationBuilder.CreateIndex(
                name: "uq_tile_orbits_orbiter_tile",
                table: "tile_orbits",
                columns: new[] { "orbiter_id", "tile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tile_reports_reporter_id",
                table: "tile_reports",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "IX_tile_reports_tile_id",
                table: "tile_reports",
                column: "tile_id");

            migrationBuilder.CreateIndex(
                name: "uq_tile_reports_user_tile",
                table: "tile_reports",
                columns: new[] { "tile_id", "reporter_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tile_views_tile_id",
                table: "tile_views",
                column: "tile_id");

            migrationBuilder.CreateIndex(
                name: "ix_tile_views_user_date",
                table: "tile_views",
                columns: new[] { "user_id", "viewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_matching_weights_user_id",
                table: "user_matching_weights",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_season_responses_season_id",
                table: "user_season_responses",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_season_responses_user_season",
                table: "user_season_responses",
                columns: new[] { "user_id", "season_id" });

            migrationBuilder.CreateIndex(
                name: "uq_user_season_pillar",
                table: "user_season_responses",
                columns: new[] { "user_id", "season_id", "pillar_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_visual_decisions_photo_embedding_id",
                table: "user_visual_decisions",
                column: "photo_embedding_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_visual_decisions_target_user_id",
                table: "user_visual_decisions",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_visual_decisions_viewer_target",
                table: "user_visual_decisions",
                columns: new[] { "viewer_user_id", "target_user_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_highlights_Users_user_id",
                table: "highlights",
                column: "user_id",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_highlights_tiles_tile_id",
                table: "highlights",
                column: "tile_id",
                principalTable: "tiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_tiles_Users_user_id",
                table: "tiles",
                column: "user_id",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_highlights_Users_user_id",
                table: "highlights");

            migrationBuilder.DropForeignKey(
                name: "FK_highlights_tiles_tile_id",
                table: "highlights");

            migrationBuilder.DropForeignKey(
                name: "FK_tiles_Users_user_id",
                table: "tiles");

            migrationBuilder.DropTable(
                name: "cf_scores");

            migrationBuilder.DropTable(
                name: "friend_bridges");

            migrationBuilder.DropTable(
                name: "moderation_queue");

            migrationBuilder.DropTable(
                name: "orbit_gravity");

            migrationBuilder.DropTable(
                name: "security_audit_log");

            migrationBuilder.DropTable(
                name: "tile_engagement");

            migrationBuilder.DropTable(
                name: "tile_orbits");

            migrationBuilder.DropTable(
                name: "tile_reports");

            migrationBuilder.DropTable(
                name: "tile_views");

            migrationBuilder.DropTable(
                name: "user_energy_meter");

            migrationBuilder.DropTable(
                name: "user_insights");

            migrationBuilder.DropTable(
                name: "user_matching_weights");

            migrationBuilder.DropTable(
                name: "user_season_responses");

            migrationBuilder.DropTable(
                name: "user_visual_decisions");

            migrationBuilder.DropTable(
                name: "user_visual_preference");

            migrationBuilder.DropTable(
                name: "user_voice_preference");

            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.DropTable(
                name: "photo_embeddings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_tiles",
                table: "tiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tiles_content_type",
                table: "tiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tiles_expires_after_created",
                table: "tiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_highlights",
                table: "highlights");

            migrationBuilder.DropCheckConstraint(
                name: "ck_highlights_slot_range",
                table: "highlights");

            migrationBuilder.DropColumn(
                name: "AttachmentProxyEmbedding",
                table: "UserVectors");

            migrationBuilder.DropColumn(
                name: "BehavioralLifestyleJson",
                table: "UserVectors");

            migrationBuilder.DropColumn(
                name: "EmotionalRhythmEmbedding",
                table: "UserVectors");

            migrationBuilder.DropColumn(
                name: "HumorEmbedding",
                table: "UserVectors");

            migrationBuilder.DropColumn(
                name: "IntentEmbedding",
                table: "UserVectors");

            migrationBuilder.DropColumn(
                name: "LifestyleEmbedding",
                table: "UserVectors");

            migrationBuilder.DropColumn(
                name: "StyleEmbedding",
                table: "UserVectors");

            migrationBuilder.DropColumn(
                name: "GhostScore",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastActiveAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TrustScore",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TrustUpdatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "voice_embedding",
                table: "tiles");

            migrationBuilder.DropColumn(
                name: "DateAgreedAt",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "DateIdeaInterestedA",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "DateIdeaInterestedAt",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "DateIdeaInterestedB",
                table: "matches");

            migrationBuilder.RenameIndex(
                name: "IX_highlights_user_id_slot_number",
                table: "highlights",
                newName: "uq_highlights_user_slot");

            migrationBuilder.AlterColumn<Vector>(
                name: "PillarEmbedding",
                table: "UserVectors",
                type: "vector(8)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_tiles",
                table: "tiles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_highlights",
                table: "highlights",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_highlights_tiles",
                table: "highlights",
                column: "tile_id",
                principalTable: "tiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_highlights_users",
                table: "highlights",
                column: "user_id",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_tiles_users",
                table: "tiles",
                column: "user_id",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
