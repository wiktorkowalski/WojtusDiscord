using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class RevokeConversationTablesFromQueryRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #444: the conversation tables hold what each person said to the assistant, DMs included,
            // and conversation_usage ties a row to its invoker. They got SELECT for the query_database
            // role through the default privileges of AddConversationQueryRole, and any member can run
            // that tool. Take the grant back. Guarded like that migration: a database without the role
            // has no grant to remove.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_query') THEN
                        REVOKE ALL ON TABLE conversation, conversation_message, conversation_usage, usage_alerts FROM wojtus_query;
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
                        GRANT SELECT ON TABLE conversation, conversation_message, conversation_usage, usage_alerts TO wojtus_query;
                    END IF;
                END
                $do$;
                """);
        }
    }
}
