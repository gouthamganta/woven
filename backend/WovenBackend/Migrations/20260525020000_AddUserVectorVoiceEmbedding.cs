using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddUserVectorVoiceEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Vector>(
                name: "VoiceEmbedding",
                table: "UserVectors",
                type: "vector(192)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoiceEmbedding",
                table: "UserVectors");
        }
    }
}
