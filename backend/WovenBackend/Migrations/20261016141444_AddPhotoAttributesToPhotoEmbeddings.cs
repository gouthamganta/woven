using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoAttributesToPhotoEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attributes_json",
                table: "photo_embeddings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confidence_scores_json",
                table: "photo_embeddings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attributes_json",
                table: "photo_embeddings");

            migrationBuilder.DropColumn(
                name: "confidence_scores_json",
                table: "photo_embeddings");
        }
    }
}
