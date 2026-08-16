using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddEchoConversations : Migration
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

            migrationBuilder.CreateIndex(
                name: "IX_echo_conversations_user_id",
                table: "echo_conversations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_echo_messages_conversation_id",
                table: "echo_messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "IX_echo_messages_created_at",
                table: "echo_messages",
                column: "created_at");

            // Insert default ECHO state
            migrationBuilder.Sql(@"
                INSERT INTO echo_state (id, current_state, state_description, updated_at)
                VALUES (1, 'flow', 'Locked in, building mode', NOW());
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "echo_messages");
            migrationBuilder.DropTable(name: "echo_conversations");
            migrationBuilder.DropTable(name: "echo_state");
        }
    }
}
