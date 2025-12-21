using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "failed_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    handler_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    event_json = table.Column<string>(type: "text", nullable: true),
                    exception_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    exception_message = table.Column<string>(type: "text", nullable: false),
                    stack_trace = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    failed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    event_received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolution_notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_failed_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guilds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    icon_hash = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    left_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guilds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "raw_event_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    event_json = table.Column<string>(type: "jsonb", nullable: false),
                    json_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_event_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "text", nullable: false),
                    global_name = table.Column<string>(type: "text", nullable: true),
                    discriminator = table.Column<string>(type: "text", nullable: true),
                    avatar_hash = table.Column<string>(type: "text", nullable: true),
                    is_bot = table.Column<bool>(type: "boolean", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    topic = table.Column<string>(type: "text", nullable: true),
                    bitrate = table.Column<int>(type: "integer", nullable: true),
                    user_limit = table.Column<int>(type: "integer", nullable: true),
                    rate_limit_per_user = table.Column<int>(type: "integer", nullable: true),
                    is_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_channels_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "emoji_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    emojis_added_json = table.Column<string>(type: "jsonb", nullable: true),
                    emojis_removed_json = table.Column<string>(type: "jsonb", nullable: true),
                    emojis_updated_json = table.Column<string>(type: "jsonb", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emoji_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_emoji_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "emotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_animated = table.Column<bool>(type: "boolean", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emotes", x => x.id);
                    table.ForeignKey(
                        name: "fk_emotes_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "guild_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    name_before = table.Column<string>(type: "text", nullable: true),
                    name_after = table.Column<string>(type: "text", nullable: true),
                    icon_hash_before = table.Column<string>(type: "text", nullable: true),
                    icon_hash_after = table.Column<string>(type: "text", nullable: true),
                    owner_discord_id_before = table.Column<long>(type: "bigint", nullable: true),
                    owner_discord_id_after = table.Column<long>(type: "bigint", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "guild_members_chunk_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    chunk_count = table.Column<int>(type: "integer", nullable: false),
                    member_count = table.Column<int>(type: "integer", nullable: false),
                    member_ids_json = table.Column<string>(type: "jsonb", nullable: true),
                    presences_json = table.Column<string>(type: "jsonb", nullable: true),
                    nonce = table.Column<string>(type: "text", nullable: true),
                    not_found_ids_json = table.Column<string>(type: "jsonb", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_members_chunk_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_members_chunk_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_syncing = table.Column<bool>(type: "boolean", nullable: false),
                    role_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    expire_behavior = table.Column<int>(type: "integer", nullable: false),
                    expire_grace_period = table.Column<int>(type: "integer", nullable: false),
                    application_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_integrations_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<int>(type: "integer", nullable: false),
                    is_hoisted = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    permissions = table.Column<long>(type: "bigint", nullable: false),
                    is_managed = table.Column<bool>(type: "boolean", nullable: false),
                    is_mentionable = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_roles_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sticker_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    stickers_added_json = table.Column<string>(type: "jsonb", nullable: true),
                    stickers_removed_json = table.Column<string>(type: "jsonb", nullable: true),
                    stickers_updated_json = table.Column<string>(type: "jsonb", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sticker_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_sticker_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "stickers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pack_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    format_type = table.Column<int>(type: "integer", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stickers", x => x.id);
                    table.ForeignKey(
                        name: "fk_stickers_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "thread_sync_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    thread_count = table.Column<int>(type: "integer", nullable: false),
                    thread_ids_json = table.Column<string>(type: "jsonb", nullable: true),
                    channel_ids_json = table.Column<string>(type: "jsonb", nullable: true),
                    members_json = table.Column<string>(type: "jsonb", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thread_sync_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_thread_sync_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "voice_server_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voice_server_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_voice_server_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    activity_type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    details = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    state = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    application_id = table.Column<long>(type: "bigint", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ends_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    large_image_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    large_image_text = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    small_image_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    small_image_text = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    party_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    party_current_size = table.Column<int>(type: "integer", nullable: true),
                    party_max_size = table.Column<int>(type: "integer", nullable: true),
                    spotify_track_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    spotify_album_art_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    spotify_album_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    spotify_artists_json = table.Column<string>(type: "jsonb", nullable: true),
                    spotify_song_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    spotify_track_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    spotify_track_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    stream_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    custom_status_emoji_id = table.Column<long>(type: "bigint", nullable: true),
                    custom_status_emoji_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    buttons_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_activities_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_activities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "audit_log_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audit_log_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    target_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    action_type = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changes_json = table.Column<string>(type: "jsonb", nullable: true),
                    options_json = table.Column<string>(type: "jsonb", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_audit_log_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "auto_mod_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    trigger_type = table.Column<int>(type: "integer", nullable: false),
                    trigger_metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    actions_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    exempt_roles_json = table.Column<string>(type: "jsonb", nullable: true),
                    exempt_channels_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_mod_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_auto_mod_rules_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_auto_mod_rules_users_creator_id",
                        column: x => x.creator_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "ban_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ban_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_ban_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_ban_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "bans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    banned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    unbanned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bans", x => x.id);
                    table.ForeignKey(
                        name: "fk_bans_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bans_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "member_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    nickname_before = table.Column<string>(type: "text", nullable: true),
                    nickname_after = table.Column<string>(type: "text", nullable: true),
                    roles_added_json = table.Column<string>(type: "jsonb", nullable: true),
                    roles_removed_json = table.Column<string>(type: "jsonb", nullable: true),
                    timeout_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ban_reason = table.Column<string>(type: "text", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_member_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_member_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nickname = table.Column<string>(type: "text", nullable: true),
                    guild_avatar_hash = table.Column<string>(type: "text", nullable: true),
                    joined_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    premium_since_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    is_muted = table.Column<bool>(type: "boolean", nullable: false),
                    is_pending = table.Column<bool>(type: "boolean", nullable: false),
                    timeout_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_members_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "presence_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    desktop_status_before = table.Column<int>(type: "integer", nullable: false),
                    mobile_status_before = table.Column<int>(type: "integer", nullable: false),
                    web_status_before = table.Column<int>(type: "integer", nullable: false),
                    desktop_status_after = table.Column<int>(type: "integer", nullable: false),
                    mobile_status_after = table.Column<int>(type: "integer", nullable: false),
                    web_status_after = table.Column<int>(type: "integer", nullable: false),
                    activities_before_json = table.Column<string>(type: "jsonb", nullable: true),
                    activities_after_json = table.Column<string>(type: "jsonb", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_presence_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_presence_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_presence_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "channel_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_type = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    name_before = table.Column<string>(type: "text", nullable: true),
                    name_after = table.Column<string>(type: "text", nullable: true),
                    topic_before = table.Column<string>(type: "text", nullable: true),
                    topic_after = table.Column<string>(type: "text", nullable: true),
                    position_before = table.Column<int>(type: "integer", nullable: true),
                    position_after = table.Column<int>(type: "integer", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_channel_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_channel_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "guild_scheduled_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<int>(type: "integer", nullable: false),
                    scheduled_start_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    scheduled_end_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    entity_metadata_location = table.Column<string>(type: "text", nullable: true),
                    user_count = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_scheduled_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_scheduled_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_guild_scheduled_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guild_scheduled_events_users_creator_id",
                        column: x => x.creator_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "invite_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inviter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    inviter_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "text", nullable: true),
                    max_age = table.Column<int>(type: "integer", nullable: true),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    is_temporary = table.Column<bool>(type: "boolean", nullable: false),
                    uses = table.Column<int>(type: "integer", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invite_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_invite_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_invite_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_invite_events_users_inviter_id",
                        column: x => x.inviter_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    code = table.Column<string>(type: "text", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inviter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    max_age = table.Column<int>(type: "integer", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    uses = table.Column<int>(type: "integer", nullable: false),
                    is_temporary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_invites_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invites_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invites_users_inviter_id",
                        column: x => x.inviter_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content = table.Column<string>(type: "text", nullable: true),
                    reply_to_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    has_attachments = table.Column<bool>(type: "boolean", nullable: false),
                    has_embeds = table.Column<bool>(type: "boolean", nullable: false),
                    attachments_json = table.Column<string>(type: "jsonb", nullable: true),
                    embeds_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    edited_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_messages_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_messages_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "pin_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    last_pin_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pin_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_pin_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_pin_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "stage_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "text", nullable: false),
                    privacy_level = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stage_instances", x => x.id);
                    table.ForeignKey(
                        name: "fk_stage_instances_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_stage_instances_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thread_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    thread_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    parent_channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    members_added_json = table.Column<string>(type: "jsonb", nullable: true),
                    members_removed_json = table.Column<string>(type: "jsonb", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thread_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_thread_events_channels_parent_channel_id",
                        column: x => x.parent_channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_thread_events_channels_thread_id",
                        column: x => x.thread_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_thread_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_thread_events_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "typing_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_typing_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_typing_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_typing_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_typing_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "voice_state_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id_before = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id_after = table.Column<Guid>(type: "uuid", nullable: true),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id_before = table.Column<long>(type: "bigint", nullable: true),
                    channel_discord_id_after = table.Column<long>(type: "bigint", nullable: true),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    was_self_muted = table.Column<bool>(type: "boolean", nullable: false),
                    was_self_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    was_server_muted = table.Column<bool>(type: "boolean", nullable: false),
                    was_server_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    was_streaming = table.Column<bool>(type: "boolean", nullable: false),
                    was_video = table.Column<bool>(type: "boolean", nullable: false),
                    was_suppressed = table.Column<bool>(type: "boolean", nullable: false),
                    is_self_muted = table.Column<bool>(type: "boolean", nullable: false),
                    is_self_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    is_server_muted = table.Column<bool>(type: "boolean", nullable: false),
                    is_server_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    is_streaming = table.Column<bool>(type: "boolean", nullable: false),
                    is_video = table.Column<bool>(type: "boolean", nullable: false),
                    is_suppressed = table.Column<bool>(type: "boolean", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voice_state_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_voice_state_events_channels_channel_id_after",
                        column: x => x.channel_id_after,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_voice_state_events_channels_channel_id_before",
                        column: x => x.channel_id_before,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_voice_state_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_voice_state_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "voice_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    is_self_muted = table.Column<bool>(type: "boolean", nullable: false),
                    is_self_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    is_server_muted = table.Column<bool>(type: "boolean", nullable: false),
                    is_server_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    is_streaming = table.Column<bool>(type: "boolean", nullable: false),
                    is_video = table.Column<bool>(type: "boolean", nullable: false),
                    is_suppressed = table.Column<bool>(type: "boolean", nullable: false),
                    joined_channel_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voice_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_voice_states_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_voice_states_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_voice_states_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_webhook_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: true),
                    avatar_hash = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhooks", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhooks_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_webhooks_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_webhooks_users_creator_id",
                        column: x => x.creator_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "integration_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    integration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    integration_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "text", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: true),
                    application_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_integration_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_integration_events_integrations_integration_id",
                        column: x => x.integration_id,
                        principalTable: "integrations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "role_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    name_before = table.Column<string>(type: "text", nullable: true),
                    name_after = table.Column<string>(type: "text", nullable: true),
                    color_before = table.Column<int>(type: "integer", nullable: true),
                    color_after = table.Column<int>(type: "integer", nullable: true),
                    permissions_before = table.Column<long>(type: "bigint", nullable: true),
                    permissions_after = table.Column<long>(type: "bigint", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_role_events_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "auto_mod_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    rule_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    rule_name = table.Column<string>(type: "text", nullable: true),
                    trigger_type = table.Column<int>(type: "integer", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    message_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    alert_system_message_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    content = table.Column<string>(type: "text", nullable: true),
                    matched_keyword = table.Column<string>(type: "text", nullable: true),
                    matched_content = table.Column<string>(type: "text", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_mod_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_auto_mod_events_auto_mod_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "auto_mod_rules",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_auto_mod_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_auto_mod_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_auto_mod_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "auto_mod_rule_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rule_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    creator_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    trigger_type = table.Column<int>(type: "integer", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: true),
                    actions_json = table.Column<string>(type: "jsonb", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_mod_rule_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_auto_mod_rule_events_auto_mod_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "auto_mod_rules",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_auto_mod_rule_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_auto_mod_rule_events_users_creator_id",
                        column: x => x.creator_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "scheduled_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    creator_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: true),
                    entity_type = table.Column<int>(type: "integer", nullable: true),
                    scheduled_start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    scheduled_end_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheduled_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_scheduled_events_guild_scheduled_events_event_id",
                        column: x => x.event_id,
                        principalTable: "guild_scheduled_events",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_scheduled_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_scheduled_events_users_creator_id",
                        column: x => x.creator_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_scheduled_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "message_edit_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    content_before = table.Column<string>(type: "text", nullable: true),
                    content_after = table.Column<string>(type: "text", nullable: true),
                    edited_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_edit_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_edit_history_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "message_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    author_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    content_before = table.Column<string>(type: "text", nullable: true),
                    has_attachments = table.Column<bool>(type: "boolean", nullable: false),
                    has_embeds = table.Column<bool>(type: "boolean", nullable: false),
                    reply_to_message_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    attachments_json = table.Column<string>(type: "jsonb", nullable: true),
                    embeds_json = table.Column<string>(type: "jsonb", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_message_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_message_events_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_message_events_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "poll_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    answer_id = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_poll_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_poll_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_poll_events_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_poll_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "reaction_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    emote_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    emote_name = table.Column<string>(type: "text", nullable: false),
                    is_animated = table.Column<bool>(type: "boolean", nullable: false),
                    is_burst = table.Column<bool>(type: "boolean", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reaction_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_reaction_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_reaction_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_reaction_events_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_reaction_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "stage_instance_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    stage_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stage_instance_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    topic_before = table.Column<string>(type: "text", nullable: true),
                    topic_after = table.Column<string>(type: "text", nullable: true),
                    privacy_level_before = table.Column<int>(type: "integer", nullable: true),
                    privacy_level_after = table.Column<int>(type: "integer", nullable: true),
                    event_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_event_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stage_instance_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_stage_instance_events_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stage_instance_events_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stage_instance_events_stage_instances_stage_instance_id",
                        column: x => x.stage_instance_id,
                        principalTable: "stage_instances",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_activities_activity_type",
                table: "activities",
                column: "activity_type");

            migrationBuilder.CreateIndex(
                name: "ix_activities_first_seen_at_utc",
                table: "activities",
                column: "first_seen_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_activities_guild_id",
                table: "activities",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_activities_user_discord_id",
                table: "activities",
                column: "user_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_activities_user_discord_id_is_active",
                table: "activities",
                columns: new[] { "user_discord_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_activities_user_id",
                table: "activities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_events_action_type",
                table: "audit_log_events",
                column: "action_type");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_events_guild_discord_id_event_timestamp_utc",
                table: "audit_log_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_events_guild_id",
                table: "audit_log_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_events_user_discord_id",
                table: "audit_log_events",
                column: "user_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_events_user_id",
                table: "audit_log_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_events_channel_id",
                table: "auto_mod_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_events_guild_discord_id_event_timestamp_utc",
                table: "auto_mod_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_events_guild_id",
                table: "auto_mod_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_events_rule_discord_id",
                table: "auto_mod_events",
                column: "rule_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_events_rule_id",
                table: "auto_mod_events",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_events_user_id",
                table: "auto_mod_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rule_events_creator_id",
                table: "auto_mod_rule_events",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rule_events_guild_discord_id_event_timestamp_utc",
                table: "auto_mod_rule_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rule_events_guild_id",
                table: "auto_mod_rule_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rule_events_rule_id",
                table: "auto_mod_rule_events",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rules_creator_id",
                table: "auto_mod_rules",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rules_discord_id",
                table: "auto_mod_rules",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rules_guild_id",
                table: "auto_mod_rules",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_ban_events_guild_discord_id_event_timestamp_utc",
                table: "ban_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_ban_events_guild_id",
                table: "ban_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_ban_events_user_discord_id",
                table: "ban_events",
                column: "user_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_ban_events_user_id",
                table: "ban_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bans_guild_id",
                table: "bans",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_bans_guild_id_is_active",
                table: "bans",
                columns: new[] { "guild_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_bans_guild_id_user_id",
                table: "bans",
                columns: new[] { "guild_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bans_user_id",
                table: "bans",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_events_channel_discord_id",
                table: "channel_events",
                column: "channel_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_events_channel_id",
                table: "channel_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_events_guild_discord_id_event_timestamp_utc",
                table: "channel_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_channel_events_guild_id",
                table: "channel_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_channels_discord_id",
                table: "channels",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channels_guild_id",
                table: "channels",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_emoji_events_guild_discord_id_event_timestamp_utc",
                table: "emoji_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_emoji_events_guild_id",
                table: "emoji_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_emotes_discord_id",
                table: "emotes",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_emotes_guild_id",
                table: "emotes",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_event_type",
                table: "failed_events",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_failed_at_utc",
                table: "failed_events",
                column: "failed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_guild_discord_id",
                table: "failed_events",
                column: "guild_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_is_resolved",
                table: "failed_events",
                column: "is_resolved");

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_is_resolved_failed_at_utc",
                table: "failed_events",
                columns: new[] { "is_resolved", "failed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_guild_events_guild_discord_id_event_timestamp_utc",
                table: "guild_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_guild_events_guild_id",
                table: "guild_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_members_chunk_events_guild_discord_id_event_timestamp",
                table: "guild_members_chunk_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_guild_members_chunk_events_guild_id",
                table: "guild_members_chunk_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_scheduled_events_channel_id",
                table: "guild_scheduled_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_scheduled_events_creator_id",
                table: "guild_scheduled_events",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_scheduled_events_discord_id",
                table: "guild_scheduled_events",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guild_scheduled_events_guild_id",
                table: "guild_scheduled_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_scheduled_events_guild_id_is_deleted",
                table: "guild_scheduled_events",
                columns: new[] { "guild_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_guilds_discord_id",
                table: "guilds",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_guild_discord_id_event_timestamp_utc",
                table: "integration_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_guild_id",
                table: "integration_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_integration_id",
                table: "integration_events",
                column: "integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_integrations_discord_id",
                table: "integrations",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integrations_guild_id",
                table: "integrations",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_invite_events_channel_id",
                table: "invite_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_invite_events_guild_discord_id_event_timestamp_utc",
                table: "invite_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_invite_events_guild_id",
                table: "invite_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_invite_events_inviter_id",
                table: "invite_events",
                column: "inviter_id");

            migrationBuilder.CreateIndex(
                name: "ix_invites_channel_id",
                table: "invites",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_invites_code",
                table: "invites",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invites_guild_id",
                table: "invites",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_invites_guild_id_is_deleted",
                table: "invites",
                columns: new[] { "guild_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_invites_inviter_id",
                table: "invites",
                column: "inviter_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_events_guild_discord_id_event_timestamp_utc",
                table: "member_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_member_events_guild_id",
                table: "member_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_events_user_discord_id",
                table: "member_events",
                column: "user_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_events_user_id",
                table: "member_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_members_guild_id",
                table: "members",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_members_user_id_guild_id",
                table: "members",
                columns: new[] { "user_id", "guild_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_message_edit_history_edited_at_utc",
                table: "message_edit_history",
                column: "edited_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_message_edit_history_message_discord_id",
                table: "message_edit_history",
                column: "message_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_edit_history_message_id",
                table: "message_edit_history",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_author_discord_id",
                table: "message_events",
                column: "author_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_author_id",
                table: "message_events",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_channel_discord_id",
                table: "message_events",
                column: "channel_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_channel_id",
                table: "message_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_guild_discord_id_event_timestamp_utc",
                table: "message_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_message_events_guild_id",
                table: "message_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_message_discord_id",
                table: "message_events",
                column: "message_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_message_id",
                table: "message_events",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_author_id",
                table: "messages",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_channel_id",
                table: "messages",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_channel_id_is_deleted",
                table: "messages",
                columns: new[] { "channel_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_discord_id",
                table: "messages",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_guild_id_created_at_utc",
                table: "messages",
                columns: new[] { "guild_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_pin_events_channel_discord_id",
                table: "pin_events",
                column: "channel_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_pin_events_channel_id",
                table: "pin_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_pin_events_guild_discord_id_event_timestamp_utc",
                table: "pin_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_pin_events_guild_id",
                table: "pin_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_events_channel_id",
                table: "poll_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_events_guild_discord_id_event_timestamp_utc",
                table: "poll_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_poll_events_guild_id",
                table: "poll_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_events_message_discord_id",
                table: "poll_events",
                column: "message_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_events_message_id",
                table: "poll_events",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_events_user_id",
                table: "poll_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_presence_events_guild_discord_id_event_timestamp_utc",
                table: "presence_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_presence_events_guild_id",
                table: "presence_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_presence_events_user_discord_id",
                table: "presence_events",
                column: "user_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_presence_events_user_id",
                table: "presence_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_event_logs_event_type",
                table: "raw_event_logs",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_raw_event_logs_guild_discord_id_received_at_utc",
                table: "raw_event_logs",
                columns: new[] { "guild_discord_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_raw_event_logs_received_at_utc",
                table: "raw_event_logs",
                column: "received_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_raw_event_logs_user_discord_id",
                table: "raw_event_logs",
                column: "user_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_events_channel_id",
                table: "reaction_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_events_guild_discord_id_event_timestamp_utc",
                table: "reaction_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_reaction_events_guild_id",
                table: "reaction_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_events_message_discord_id",
                table: "reaction_events",
                column: "message_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_events_message_id",
                table: "reaction_events",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_events_user_id",
                table: "reaction_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_events_guild_discord_id_event_timestamp_utc",
                table: "role_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_role_events_guild_id",
                table: "role_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_events_role_id",
                table: "role_events",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_discord_id",
                table: "roles",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_guild_id",
                table: "roles",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_channel_id",
                table: "scheduled_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_creator_id",
                table: "scheduled_events",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_event_discord_id",
                table: "scheduled_events",
                column: "event_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_event_id",
                table: "scheduled_events",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_guild_discord_id_event_timestamp_utc",
                table: "scheduled_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_guild_id",
                table: "scheduled_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_user_id",
                table: "scheduled_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_instance_events_channel_id",
                table: "stage_instance_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_instance_events_guild_discord_id_event_timestamp_utc",
                table: "stage_instance_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_stage_instance_events_guild_id",
                table: "stage_instance_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_instance_events_stage_instance_id",
                table: "stage_instance_events",
                column: "stage_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_instances_channel_id",
                table: "stage_instances",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_instances_discord_id",
                table: "stage_instances",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stage_instances_guild_id",
                table: "stage_instances",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_sticker_events_guild_discord_id_event_timestamp_utc",
                table: "sticker_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_sticker_events_guild_id",
                table: "sticker_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_stickers_discord_id",
                table: "stickers",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stickers_guild_id",
                table: "stickers",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_events_guild_discord_id_event_timestamp_utc",
                table: "thread_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_thread_events_guild_id",
                table: "thread_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_events_owner_id",
                table: "thread_events",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_events_parent_channel_discord_id",
                table: "thread_events",
                column: "parent_channel_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_events_parent_channel_id",
                table: "thread_events",
                column: "parent_channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_events_thread_id",
                table: "thread_events",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_sync_events_guild_discord_id_event_timestamp_utc",
                table: "thread_sync_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_thread_sync_events_guild_id",
                table: "thread_sync_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_typing_events_channel_id",
                table: "typing_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_typing_events_guild_discord_id_received_at_utc",
                table: "typing_events",
                columns: new[] { "guild_discord_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_typing_events_guild_id",
                table: "typing_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_typing_events_user_discord_id",
                table: "typing_events",
                column: "user_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_typing_events_user_id",
                table: "typing_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_discord_id",
                table: "users",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_voice_server_events_guild_discord_id_event_timestamp_utc",
                table: "voice_server_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_voice_server_events_guild_id",
                table: "voice_server_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_state_events_channel_id_after",
                table: "voice_state_events",
                column: "channel_id_after");

            migrationBuilder.CreateIndex(
                name: "ix_voice_state_events_channel_id_before",
                table: "voice_state_events",
                column: "channel_id_before");

            migrationBuilder.CreateIndex(
                name: "ix_voice_state_events_guild_discord_id_event_timestamp_utc",
                table: "voice_state_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_voice_state_events_guild_id",
                table: "voice_state_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_state_events_user_discord_id",
                table: "voice_state_events",
                column: "user_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_state_events_user_id",
                table: "voice_state_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_states_channel_id",
                table: "voice_states",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_states_guild_id",
                table: "voice_states",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_states_user_id_guild_id",
                table: "voice_states",
                columns: new[] { "user_id", "guild_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_channel_id",
                table: "webhook_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_guild_discord_id_event_timestamp_utc",
                table: "webhook_events",
                columns: new[] { "guild_discord_id", "event_timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_guild_id",
                table: "webhook_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhooks_channel_id",
                table: "webhooks",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhooks_creator_id",
                table: "webhooks",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhooks_discord_id",
                table: "webhooks",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhooks_guild_id",
                table: "webhooks",
                column: "guild_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activities");

            migrationBuilder.DropTable(
                name: "audit_log_events");

            migrationBuilder.DropTable(
                name: "auto_mod_events");

            migrationBuilder.DropTable(
                name: "auto_mod_rule_events");

            migrationBuilder.DropTable(
                name: "ban_events");

            migrationBuilder.DropTable(
                name: "bans");

            migrationBuilder.DropTable(
                name: "channel_events");

            migrationBuilder.DropTable(
                name: "emoji_events");

            migrationBuilder.DropTable(
                name: "emotes");

            migrationBuilder.DropTable(
                name: "failed_events");

            migrationBuilder.DropTable(
                name: "guild_events");

            migrationBuilder.DropTable(
                name: "guild_members_chunk_events");

            migrationBuilder.DropTable(
                name: "integration_events");

            migrationBuilder.DropTable(
                name: "invite_events");

            migrationBuilder.DropTable(
                name: "invites");

            migrationBuilder.DropTable(
                name: "member_events");

            migrationBuilder.DropTable(
                name: "members");

            migrationBuilder.DropTable(
                name: "message_edit_history");

            migrationBuilder.DropTable(
                name: "message_events");

            migrationBuilder.DropTable(
                name: "pin_events");

            migrationBuilder.DropTable(
                name: "poll_events");

            migrationBuilder.DropTable(
                name: "presence_events");

            migrationBuilder.DropTable(
                name: "raw_event_logs");

            migrationBuilder.DropTable(
                name: "reaction_events");

            migrationBuilder.DropTable(
                name: "role_events");

            migrationBuilder.DropTable(
                name: "scheduled_events");

            migrationBuilder.DropTable(
                name: "stage_instance_events");

            migrationBuilder.DropTable(
                name: "sticker_events");

            migrationBuilder.DropTable(
                name: "stickers");

            migrationBuilder.DropTable(
                name: "thread_events");

            migrationBuilder.DropTable(
                name: "thread_sync_events");

            migrationBuilder.DropTable(
                name: "typing_events");

            migrationBuilder.DropTable(
                name: "voice_server_events");

            migrationBuilder.DropTable(
                name: "voice_state_events");

            migrationBuilder.DropTable(
                name: "voice_states");

            migrationBuilder.DropTable(
                name: "webhook_events");

            migrationBuilder.DropTable(
                name: "webhooks");

            migrationBuilder.DropTable(
                name: "auto_mod_rules");

            migrationBuilder.DropTable(
                name: "integrations");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "guild_scheduled_events");

            migrationBuilder.DropTable(
                name: "stage_instances");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "guilds");
        }
    }
}
