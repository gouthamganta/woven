using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateExposureAndSignal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CandidateExposures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ViewerUserId = table.Column<int>(type: "integer", nullable: false),
                    ShownUserId = table.Column<int>(type: "integer", nullable: false),
                    Surface = table.Column<string>(type: "text", nullable: false),
                    Bucket = table.Column<string>(type: "text", nullable: true),
                    ScoreSnapshot = table.Column<double>(type: "double precision", nullable: true),
                    DateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateExposures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CandidateSignals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromUserId = table.Column<int>(type: "integer", nullable: false),
                    ToUserId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    MetaJson = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateSignals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateExposures_ShownUserId_CreatedAt",
                table: "CandidateExposures",
                columns: new[] { "ShownUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateExposures_ViewerUserId_DateUtc",
                table: "CandidateExposures",
                columns: new[] { "ViewerUserId", "DateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateExposures_ViewerUserId_ShownUserId_DateUtc_Surface",
                table: "CandidateExposures",
                columns: new[] { "ViewerUserId", "ShownUserId", "DateUtc", "Surface" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSignals_FromUserId_ToUserId_Type_CreatedAt",
                table: "CandidateSignals",
                columns: new[] { "FromUserId", "ToUserId", "Type", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSignals_ToUserId_ExpiresAt",
                table: "CandidateSignals",
                columns: new[] { "ToUserId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateExposures");

            migrationBuilder.DropTable(
                name: "CandidateSignals");
        }
    }
}
