using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;
using WovenBackend.Data;

#nullable disable

namespace WovenBackend.Migrations
{
    [DbContext(typeof(WovenDbContext))]
    [Migration("20260516000001_AddTilesAndHighlights")]
    public partial class AddTilesAndHighlights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE "tiles" (
                    id                uuid                     NOT NULL DEFAULT gen_random_uuid(),
                    user_id           integer                  NOT NULL,
                    content_type      character varying(20)    NOT NULL,
                    content_text      text,
                    media_url         character varying(2048),
                    embedding         vector(1536),
                    created_at        timestamptz              NOT NULL DEFAULT now(),
                    expires_at        timestamptz              NOT NULL,
                    is_expired        boolean                  NOT NULL DEFAULT false,
                    is_highlighted    boolean                  NOT NULL DEFAULT false,
                    is_moderated      boolean                  NOT NULL DEFAULT false,
                    CONSTRAINT pk_tiles PRIMARY KEY (id),
                    CONSTRAINT ck_tiles_content_type CHECK (content_type IN ('text','photo','video','voice')),
                    CONSTRAINT ck_tiles_expires_after_created CHECK (expires_at > created_at),
                    CONSTRAINT fk_tiles_users FOREIGN KEY (user_id) REFERENCES "Users" ("Id") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("""CREATE INDEX "IX_tiles_user_id_is_expired" ON "tiles" (user_id, is_expired);""");
            migrationBuilder.Sql("""CREATE INDEX "IX_tiles_expires_at_is_expired" ON "tiles" (expires_at, is_expired);""");
            migrationBuilder.Sql("""CREATE INDEX "IX_tiles_is_moderated_is_expired_created_at" ON "tiles" (is_moderated, is_expired, created_at);""");
            migrationBuilder.Sql("""CREATE INDEX "IX_tiles_embedding" ON "tiles" USING hnsw (embedding vector_cosine_ops) WHERE embedding IS NOT NULL;""");

            migrationBuilder.Sql("""
                CREATE TABLE "highlights" (
                    id          uuid        NOT NULL DEFAULT gen_random_uuid(),
                    user_id     integer     NOT NULL,
                    tile_id     uuid        NOT NULL,
                    slot_number integer     NOT NULL,
                    pinned_at   timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_highlights PRIMARY KEY (id),
                    CONSTRAINT ck_highlights_slot_range CHECK (slot_number BETWEEN 1 AND 9),
                    CONSTRAINT uq_highlights_user_slot UNIQUE (user_id, slot_number),
                    CONSTRAINT fk_highlights_users FOREIGN KEY (user_id) REFERENCES "Users" ("Id") ON DELETE CASCADE,
                    CONSTRAINT fk_highlights_tiles FOREIGN KEY (tile_id) REFERENCES "tiles" (id) ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("""CREATE INDEX "IX_highlights_user_id" ON "highlights" (user_id);""");
            migrationBuilder.Sql("""CREATE INDEX "IX_highlights_tile_id" ON "highlights" (tile_id);""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "highlights";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "tiles";""");
        }
    }
}
