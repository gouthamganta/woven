using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    public partial class AddAntiGhostingAndActivity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "GhostScore",
                table: "Users",
                type: "real",
                nullable: false,
                defaultValue: 0.5f);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActiveAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DateIdeaInterestedA",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DateIdeaInterestedB",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DateIdeaInterestedAt",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DateAgreedAt",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "GhostScore", table: "Users");
            migrationBuilder.DropColumn(name: "LastActiveAt", table: "Users");
            migrationBuilder.DropColumn(name: "DateIdeaInterestedA", table: "matches");
            migrationBuilder.DropColumn(name: "DateIdeaInterestedB", table: "matches");
            migrationBuilder.DropColumn(name: "DateIdeaInterestedAt", table: "matches");
            migrationBuilder.DropColumn(name: "DateAgreedAt", table: "matches");
        }
    }
}
