using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;
using WovenBackend.Data;

#nullable disable

namespace WovenBackend.Migrations
{
    [DbContext(typeof(WovenDbContext))]
    [Migration("20260516000002_AddExpressionEmbedding")]
    public partial class AddExpressionEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Vector>(
                name: "ExpressionEmbedding",
                table: "UserVectors",
                type: "vector(1536)",
                nullable: true);

            migrationBuilder.Sql(
                """CREATE INDEX "IX_UserVectors_ExpressionEmbedding" ON "UserVectors" USING hnsw ("ExpressionEmbedding" vector_cosine_ops) WHERE "ExpressionEmbedding" IS NOT NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_UserVectors_ExpressionEmbedding";""");

            migrationBuilder.DropColumn(
                name: "ExpressionEmbedding",
                table: "UserVectors");
        }
    }
}
