using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class Moments_MomentResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "moment_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_utc = table.Column<DateOnly>(type: "date", nullable: false),
                    from_user_id = table.Column<int>(type: "integer", nullable: false),
                    to_user_id = table.Column<int>(type: "integer", nullable: false),
                    choice = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_responses", x => x.id);
                    table.CheckConstraint("ck_moment_responses_no_self", "\"from_user_id\" <> \"to_user_id\"");
                    table.ForeignKey(
                        name: "FK_moment_responses_Users_from_user_id",
                        column: x => x.from_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_moment_responses_Users_to_user_id",
                        column: x => x.to_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_moment_responses_date_utc_from_user_id_to_user_id",
                table: "moment_responses",
                columns: new[] { "date_utc", "from_user_id", "to_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_moment_responses_from_user_id",
                table: "moment_responses",
                column: "from_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_moment_responses_to_user_id",
                table: "moment_responses",
                column: "to_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "moment_responses");
        }
    }
}
