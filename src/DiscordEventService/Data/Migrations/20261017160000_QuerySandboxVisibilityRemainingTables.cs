using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class QuerySandboxVisibilityRemainingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #468: the rest of #460. The same query_sandbox_visible policy on the other tables that carry
            // deleted or hidden-channel content, built on the QuerySandboxVisibility functions. Guarded like
            // that migration; the app login still bypasses RLS, so ingestion and meme_search (which runs on
            // the app login) are untouched.
            //
            // failed_events.event_json is a whole serialized event, message text included, that no row
            // filter can clean: revoked, like raw_event_logs. poll_events and reaction_events carry message
            // and channel ids, so they take message_events' policy. pin_events has only a channel. meme_index
            // follows its message through messages' own policy, and meme_annotations follows its meme_index
            // row (a policy's subquery is filtered by the queried table's RLS too).
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_query') THEN
                        REVOKE ALL ON TABLE failed_events FROM wojtus_query;

                        ALTER TABLE poll_events ENABLE ROW LEVEL SECURITY;
                        DROP POLICY IF EXISTS query_sandbox_visible ON poll_events;
                        CREATE POLICY query_sandbox_visible ON poll_events FOR SELECT TO wojtus_query
                            USING (message_discord_id NOT IN (SELECT unnest(public.query_sandbox_deleted_message_ids()))
                                   AND channel_discord_id NOT IN (SELECT unnest(public.query_sandbox_hidden_channel_ids())));

                        ALTER TABLE reaction_events ENABLE ROW LEVEL SECURITY;
                        DROP POLICY IF EXISTS query_sandbox_visible ON reaction_events;
                        CREATE POLICY query_sandbox_visible ON reaction_events FOR SELECT TO wojtus_query
                            USING (message_discord_id NOT IN (SELECT unnest(public.query_sandbox_deleted_message_ids()))
                                   AND channel_discord_id NOT IN (SELECT unnest(public.query_sandbox_hidden_channel_ids())));

                        ALTER TABLE pin_events ENABLE ROW LEVEL SECURITY;
                        DROP POLICY IF EXISTS query_sandbox_visible ON pin_events;
                        CREATE POLICY query_sandbox_visible ON pin_events FOR SELECT TO wojtus_query
                            USING (channel_discord_id NOT IN (SELECT unnest(public.query_sandbox_hidden_channel_ids())));

                        ALTER TABLE meme_index ENABLE ROW LEVEL SECURITY;
                        DROP POLICY IF EXISTS query_sandbox_visible ON meme_index;
                        CREATE POLICY query_sandbox_visible ON meme_index FOR SELECT TO wojtus_query
                            USING (EXISTS (SELECT 1 FROM public.messages m WHERE m.id = meme_index.message_id));

                        ALTER TABLE meme_annotations ENABLE ROW LEVEL SECURITY;
                        DROP POLICY IF EXISTS query_sandbox_visible ON meme_annotations;
                        CREATE POLICY query_sandbox_visible ON meme_annotations FOR SELECT TO wojtus_query
                            USING (EXISTS (SELECT 1 FROM public.meme_index mi WHERE mi.id = meme_annotations.meme_index_id));
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS query_sandbox_visible ON poll_events;
                DROP POLICY IF EXISTS query_sandbox_visible ON reaction_events;
                DROP POLICY IF EXISTS query_sandbox_visible ON pin_events;
                DROP POLICY IF EXISTS query_sandbox_visible ON meme_index;
                DROP POLICY IF EXISTS query_sandbox_visible ON meme_annotations;
                ALTER TABLE poll_events DISABLE ROW LEVEL SECURITY;
                ALTER TABLE reaction_events DISABLE ROW LEVEL SECURITY;
                ALTER TABLE pin_events DISABLE ROW LEVEL SECURITY;
                ALTER TABLE meme_index DISABLE ROW LEVEL SECURITY;
                ALTER TABLE meme_annotations DISABLE ROW LEVEL SECURITY;

                DO $do$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_query') THEN
                        GRANT SELECT ON TABLE failed_events TO wojtus_query;
                    END IF;
                END
                $do$;
                """);
        }
    }
}
