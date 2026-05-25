using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P4_5_NormalizeEmptyContentToNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE messages SET content = NULL WHERE content = '';");
            migrationBuilder.Sql("UPDATE message_events SET content = NULL WHERE content = '';");
            migrationBuilder.Sql("UPDATE message_events SET content_before = NULL WHERE content_before = '';");
            migrationBuilder.Sql("UPDATE message_edit_history SET content_before = NULL WHERE content_before = '';");
            migrationBuilder.Sql("UPDATE message_edit_history SET content_after = NULL WHERE content_after = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the Up migration merged two representations of "no content"
            // (NULL and '') into one (NULL). Reversing this would clobber rows that were
            // legitimately NULL before the migration ran.
        }
    }
}
