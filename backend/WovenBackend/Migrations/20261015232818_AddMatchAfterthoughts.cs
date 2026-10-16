using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchAfterthoughts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "match_afterthoughts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<int>(type: "integer", nullable: false),
                    about_user_id = table.Column<int>(type: "integer", nullable: false),
                    close_path = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rating = table.Column<short>(type: "smallint", nullable: true),
                    reason_tag = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    note_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_safety = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_afterthoughts", x => x.id);
                    table.ForeignKey(
                        name: "FK_match_afterthoughts_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_match_afterthoughts_match_id",
                table: "match_afterthoughts",
                column: "match_id");

            // Unique constraint to prevent duplicate afterthoughts from same user for same match
            migrationBuilder.CreateIndex(
                name: "IX_match_afterthoughts_match_id_from_user_id",
                table: "match_afterthoughts",
                columns: new[] { "match_id", "from_user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_afterthoughts");
        }
    }
}
