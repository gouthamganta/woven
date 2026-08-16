using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddLinUcbUserModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LinUcbUserModels",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Dim = table.Column<int>(type: "integer", nullable: false, defaultValue: 24),
                    AInvJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    BJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    ObservationCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinUcbUserModels", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_LinUcbUserModels_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LinUcbUserModels");
        }
    }
}
