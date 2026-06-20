using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationQueryRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // query_database (#238 §4) drops into this non-superuser, SELECT-only role via SET LOCAL ROLE,
            // so a prompt-injected query can neither write nor reach privileged/file functions (the app
            // login is a superuser; a read-only txn alone would not stop pg_read_file etc.). Scoped to the
            // public schema so it cannot read system catalogs (e.g. pg_authid password hashes) — unlike the
            // built-in pg_read_all_data. Runs as the migration owner so ALTER DEFAULT PRIVILEGES covers
            // future tables; guarded so a de-privileged app login skips this instead of failing startup;
            // idempotent. The role name is fixed here and must match ConversationOptions.QueryRoleName.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_user AND (rolsuper OR rolcreaterole)) THEN
                        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_query') THEN
                            CREATE ROLE wojtus_query NOSUPERUSER NOLOGIN NOCREATEDB NOCREATEROLE;
                        END IF;
                        GRANT USAGE ON SCHEMA public TO wojtus_query;
                        REVOKE CREATE ON SCHEMA public FROM wojtus_query;
                        GRANT SELECT ON ALL TABLES IN SCHEMA public TO wojtus_query;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO wojtus_query;
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
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_user AND (rolsuper OR rolcreaterole))
                       AND EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_query') THEN
                        EXECUTE 'DROP OWNED BY wojtus_query';
                        DROP ROLE wojtus_query;
                    END IF;
                END
                $do$;
                """);
        }
    }
}
