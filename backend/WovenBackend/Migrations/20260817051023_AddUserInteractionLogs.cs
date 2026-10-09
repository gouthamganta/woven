using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddUserInteractionLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "echo_conversations",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_echo_conversations", x => x.conversation_id);
                });

            migrationBuilder.CreateTable(
                name: "echo_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    current_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_echo_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_interaction_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    context = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_interaction_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_interaction_logs_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "echo_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    voice_audio_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    echo_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    citations_json = table.Column<string>(type: "text", nullable: true),
                    live_stats_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_echo_messages", x => x.message_id);
                    table.ForeignKey(
                        name: "FK_echo_messages_echo_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "echo_conversations",
                        principalColumn: "conversation_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_echo_messages_conversation_id",
                table: "echo_messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_interaction_logs_occurred",
                table: "user_interaction_logs",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_interaction_logs_user_event",
                table: "user_interaction_logs",
                columns: new[] { "user_id", "event_type" });

            migrationBuilder.CreateIndex(
                name: "ix_interaction_logs_user_time",
                table: "user_interaction_logs",
                columns: new[] { "user_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "echo_messages");

            migrationBuilder.DropTable(
                name: "echo_state");

            migrationBuilder.DropTable(
                name: "user_interaction_logs");

            migrationBuilder.DropTable(
                name: "echo_conversations");
        }
    }
}
