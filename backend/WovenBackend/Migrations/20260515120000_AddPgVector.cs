using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;
using WovenBackend.Data;

#nullable disable

namespace WovenBackend.Migrations
{
    [DbContext(typeof(WovenDbContext))]
    [Migration("20260515120000_AddPgVector")]
    public partial class AddPgVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enable pgvector extension — idempotent, safe on a live database.
            // Required before any vector column or operator can be used.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            // Add 8-dim pillar embedding column (nullable — existing rows keep NULL
            // until UserVectorBuilder.BuildAndSaveV1Async runs for them again).
            migrationBuilder.AddColumn<Vector>(
                name: "PillarEmbedding",
                table: "UserVectors",
                type: "vector(8)",
                nullable: true);

            // HNSW index for cosine distance ANN queries (Phase 3B).
            // HNSW chosen over IVFFlat: works from empty/sparse tables with no training step.
            // vector_cosine_ops = optimised for <=> (cosine distance) operator.
            migrationBuilder.Sql(
                """CREATE INDEX "IX_UserVectors_PillarEmbedding" ON "UserVectors" USING hnsw ("PillarEmbedding" vector_cosine_ops);""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_UserVectors_PillarEmbedding";""");

            migrationBuilder.DropColumn(
                name: "PillarEmbedding",
                table: "UserVectors");

            // NOTE: the vector extension is intentionally NOT dropped here.
            // Dropping it would require removing all vector columns across every table
            // that may be added in Phase 2/3. Drop manually only if rolling back entirely.
        }
    }
}
