using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddAfterthoughtProcessedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_match_afterthoughts_match_id",
                table: "match_afterthoughts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processed_at",
                table: "match_afterthoughts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "processed_at",
                table: "match_afterthoughts");

            migrationBuilder.CreateIndex(
                name: "IX_match_afterthoughts_match_id",
                table: "match_afterthoughts",
                column: "match_id");
        }
    }
}
