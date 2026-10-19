using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WovenBackend.Migrations
{
    /// <inheritdoc />
    public partial class MakeCoachingDeliveredAtNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL to handle snake_case column name
            migrationBuilder.Sql("ALTER TABLE coaching_summaries ALTER COLUMN delivered_at DROP NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL to handle snake_case column name
            migrationBuilder.Sql("ALTER TABLE coaching_summaries ALTER COLUMN delivered_at SET NOT NULL;");
        }
    }
}
