using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicIntake48h : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserDynamicIntakeSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CycleId = table.Column<string>(type: "text", nullable: false),
                    CycleStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CycleEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VariantJson = table.Column<string>(type: "text", nullable: false),
                    AnswersJson = table.Column<string>(type: "text", nullable: false),
                    FeaturesJson = table.Column<string>(type: "text", nullable: false),
                    MappingVersion = table.Column<int>(type: "integer", nullable: false),
                    VariantSource = table.Column<string>(type: "text", nullable: false),
                    GenerationMetaJson = table.Column<string>(type: "text", nullable: false),
                    AnsweredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDynamicIntakeSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDynamicIntakeSets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserDynamicIntakeSets_UserId_CycleId",
                table: "UserDynamicIntakeSets",
                columns: new[] { "UserId", "CycleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDynamicIntakeSets");
        }
    }
}
