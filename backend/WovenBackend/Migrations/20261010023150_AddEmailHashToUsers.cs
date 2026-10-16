using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailHashToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailHash",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Add index for fast email lookup
            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailHash",
                table: "Users",
                column: "EmailHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_EmailHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailHash",
                table: "Users");
        }
    }
}
