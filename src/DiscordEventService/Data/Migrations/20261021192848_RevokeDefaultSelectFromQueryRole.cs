using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class RevokeDefaultSelectFromQueryRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #388: AddConversationQueryRole let every later CREATE TABLE in public grant SELECT to the
            // query role, so a new table was readable by query_database with no line that said so. Drop
            // that default; a migration that wants its table readable now grants it. Existing grants stay
            // as they are. The default belongs to the login that ran AddConversationQueryRole, the same
            // login that runs this one. Guarded like that migration.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_query') THEN
                        ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE SELECT ON TABLES FROM wojtus_query;
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_query') THEN
                        ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO wojtus_query;
                    END IF;
                END
                $do$;
                """);
        }
    }
}
