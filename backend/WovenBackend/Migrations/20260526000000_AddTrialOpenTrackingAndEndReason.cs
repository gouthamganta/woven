using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    public partial class AddTrialOpenTrackingAndEndReason : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_user_a_opened_at",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_user_b_opened_at",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trial_end_reason",
                table: "matches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "trial_user_a_opened_at", table: "matches");
            migrationBuilder.DropColumn(name: "trial_user_b_opened_at", table: "matches");
            migrationBuilder.DropColumn(name: "trial_end_reason", table: "matches");
        }
    }
}
