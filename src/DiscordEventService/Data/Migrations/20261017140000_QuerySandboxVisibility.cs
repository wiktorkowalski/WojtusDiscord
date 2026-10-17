using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class QuerySandboxVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #460: query_database hides deleted messages and configured channels from members. The model
            // writes the SQL, so the filter is row-level security on the query role, not a WHERE in a
            // prompt. The switches live in two tables the app writes at boot from Conversation:QueryDatabase
            // (QuerySandboxSettingsWriter); the query role gets no privilege on them, and its transaction is
            // read only. A GUC would not hold: any SELECT can call set_config. Not EF entities, so they stay
            // out of the schema catalog and the hint.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS query_sandbox_settings (
                    id smallint PRIMARY KEY CHECK (id = 1),
                    exclude_deleted_messages boolean NOT NULL DEFAULT true,
                    updated_at_utc timestamptz NOT NULL DEFAULT now()
                );
                INSERT INTO query_sandbox_settings (id) VALUES (1) ON CONFLICT (id) DO NOTHING;

                CREATE TABLE IF NOT EXISTS query_sandbox_hidden_channels (
                    channel_discord_id bigint PRIMARY KEY
                );
                """);

            // SECURITY DEFINER so a policy evaluated as the query role can read the settings that role
            // cannot SELECT. The policies test membership as "NOT IN (SELECT unnest(…))": an uncorrelated
            // subquery, which Postgres runs once per query as a hashed SubPlan, so each row costs one hash
            // probe (an "<> ALL (array)" over an InitPlan would scan the whole array per row). Owned by the
            // migrating login (superuser or table owner), so the lookups inside bypass RLS and cannot
            // recurse. A missing settings row reads as "exclude": the default fails closed.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.query_sandbox_excludes_deleted() RETURNS boolean
                LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
                    SELECT COALESCE((SELECT exclude_deleted_messages FROM public.query_sandbox_settings WHERE id = 1), true)
                $fn$;

                -- A listed id hides everything under it too: a category its channels, a channel its threads.
                CREATE OR REPLACE FUNCTION public.query_sandbox_hidden_channel_ids() RETURNS bigint[]
                LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
                    WITH RECURSIVE hidden(discord_id) AS (
                        SELECT channel_discord_id FROM public.query_sandbox_hidden_channels
                        UNION
                        SELECT c.discord_id FROM public.channels c JOIN hidden h ON c.parent_discord_id = h.discord_id
                    )
                    SELECT COALESCE(array_agg(discord_id), '{}') FROM hidden
                $fn$;

                CREATE OR REPLACE FUNCTION public.query_sandbox_hidden_channel_uuids() RETURNS uuid[]
                LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
                    SELECT COALESCE(array_agg(id), '{}') FROM public.channels
                    WHERE discord_id = ANY (public.query_sandbox_hidden_channel_ids())
                $fn$;

                -- Deleted per the message row, or per a delete event when the row never existed (the bot
                -- saw only the delete). Deletions are rare, so the array stays small.
                CREATE OR REPLACE FUNCTION public.query_sandbox_deleted_message_ids() RETURNS bigint[]
                LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
                    SELECT CASE WHEN public.query_sandbox_excludes_deleted() THEN COALESCE((
                        SELECT array_agg(DISTINCT discord_id) FROM (
                            SELECT discord_id FROM public.messages WHERE is_deleted
                            UNION ALL
                            SELECT message_discord_id FROM public.message_events WHERE event_type IN (2, 3)
                        ) d), '{}') ELSE '{}' END
                $fn$;
                """);

            // Policies only where the query role exists (same guard as AddConversationQueryRole). Enabling
            // RLS does not touch ingestion: the app login owns these tables (and is a superuser), and owners
            // and superusers bypass RLS; FORCE is deliberately not set. message_edit_history and
            // message_mentions follow their message through messages' own policy (a policy's subquery is
            // filtered by the queried table's RLS too). message_events and auto_mod_events carry ids only, so
            // they check the id sets. raw_event_logs holds whole payloads that cannot be row-filtered by deletion, so the
            // role loses it entirely.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_query') THEN
                        REVOKE ALL ON TABLE query_sandbox_settings, query_sandbox_hidden_channels FROM wojtus_query, PUBLIC;
                        REVOKE ALL ON TABLE raw_event_logs FROM wojtus_query;

                        ALTER TABLE messages ENABLE ROW LEVEL SECURITY;
                        DROP POLICY IF EXISTS query_sandbox_visible ON messages;
                        CREATE POLICY query_sandbox_visible ON messages FOR SELECT TO wojtus_query
                            USING ((NOT is_deleted OR NOT (SELECT public.query_sandbox_excludes_deleted()))
                                   AND channel_id NOT IN (SELECT unnest(public.query_sandbox_hidden_channel_uuids())));

                        ALTER TABLE message_edit_history ENABLE ROW LEVEL SECURITY;
                        DROP POLICY IF EXISTS query_sandbox_visible ON message_edit_history;
                        CREATE POLICY query_sandbox_visible ON message_edit_history FOR SELECT TO wojtus_query
                            USING (EXISTS (SELECT 1 FROM public.messages m WHERE m.id = message_edit_history.message_id));

                        ALTER TABLE message_mentions ENABLE ROW LEVEL SECURITY;
                        DROP POLICY IF EXISTS query_sandbox_visible ON message_mentions;
                        CREATE POLICY query_sandbox_visible ON message_mentions FOR SELECT TO wojtus_query
                            USING (EXISTS (SELECT 1 FROM public.messages m WHERE m.id = message_mentions.message_id));

                        ALTER TABLE message_events ENABLE ROW LEVEL SECURITY;
                        DROP POLICY IF EXISTS query_sandbox_visible ON message_events;
                DROP POLICY IF EXISTS query_sandbox_visible ON auto_mod_events;
                        CREATE POLICY query_sandbox_visible ON message_events FOR SELECT TO wojtus_query
                            USING (message_discord_id NOT IN (SELECT unnest(public.query_sandbox_deleted_message_ids()))
                                   AND channel_discord_id NOT IN (SELECT unnest(public.query_sandbox_hidden_channel_ids())));

                        -- AutoMod keeps the text it flagged, keyed by channel and (when not blocked) message.
                        ALTER TABLE auto_mod_events ENABLE ROW LEVEL SECURITY;
                        DROP POLICY IF EXISTS query_sandbox_visible ON auto_mod_events;
                        CREATE POLICY query_sandbox_visible ON auto_mod_events FOR SELECT TO wojtus_query
                            USING ((message_discord_id IS NULL
                                    OR message_discord_id NOT IN (SELECT unnest(public.query_sandbox_deleted_message_ids())))
                                   AND (channel_discord_id IS NULL
                                    OR channel_discord_id NOT IN (SELECT unnest(public.query_sandbox_hidden_channel_ids()))));
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS query_sandbox_visible ON messages;
                DROP POLICY IF EXISTS query_sandbox_visible ON message_edit_history;
                DROP POLICY IF EXISTS query_sandbox_visible ON message_mentions;
                DROP POLICY IF EXISTS query_sandbox_visible ON message_events;
                DROP POLICY IF EXISTS query_sandbox_visible ON auto_mod_events;
                ALTER TABLE messages DISABLE ROW LEVEL SECURITY;
                ALTER TABLE message_edit_history DISABLE ROW LEVEL SECURITY;
                ALTER TABLE message_mentions DISABLE ROW LEVEL SECURITY;
                ALTER TABLE message_events DISABLE ROW LEVEL SECURITY;
                ALTER TABLE auto_mod_events DISABLE ROW LEVEL SECURITY;

                DROP FUNCTION IF EXISTS public.query_sandbox_deleted_message_ids();
                DROP FUNCTION IF EXISTS public.query_sandbox_hidden_channel_uuids();
                DROP FUNCTION IF EXISTS public.query_sandbox_hidden_channel_ids();
                DROP FUNCTION IF EXISTS public.query_sandbox_excludes_deleted();
                DROP TABLE IF EXISTS query_sandbox_hidden_channels;
                DROP TABLE IF EXISTS query_sandbox_settings;

                DO $do$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_query') THEN
                        GRANT SELECT ON TABLE raw_event_logs TO wojtus_query;
                    END IF;
                END
                $do$;
                """);
        }
    }
}
