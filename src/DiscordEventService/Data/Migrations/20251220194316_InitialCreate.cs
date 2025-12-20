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
                name: "Guilds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IconHash = table.Column<string>(type: "text", nullable: true),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    LeftAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guilds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawEventLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    EventJson = table.Column<string>(type: "jsonb", nullable: false),
                    JsonSizeBytes = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawEventLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    GlobalName = table.Column<string>(type: "text", nullable: true),
                    Discriminator = table.Column<string>(type: "text", nullable: true),
                    AvatarHash = table.Column<string>(type: "text", nullable: true),
                    IsBot = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutoModRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    TriggerMetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    ActionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ExemptRolesJson = table.Column<string>(type: "jsonb", nullable: true),
                    ExemptChannelsJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoModRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutoModRules_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Topic = table.Column<string>(type: "text", nullable: true),
                    Bitrate = table.Column<int>(type: "integer", nullable: true),
                    UserLimit = table.Column<int>(type: "integer", nullable: true),
                    RateLimitPerUser = table.Column<int>(type: "integer", nullable: true),
                    IsNsfw = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Channels_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmojiEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EmojisAddedJson = table.Column<string>(type: "jsonb", nullable: true),
                    EmojisRemovedJson = table.Column<string>(type: "jsonb", nullable: true),
                    EmojisUpdatedJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmojiEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmojiEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Emotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsAnimated = table.Column<bool>(type: "boolean", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Emotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Emotes_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GuildEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    NameBefore = table.Column<string>(type: "text", nullable: true),
                    NameAfter = table.Column<string>(type: "text", nullable: true),
                    IconHashBefore = table.Column<string>(type: "text", nullable: true),
                    IconHashAfter = table.Column<string>(type: "text", nullable: true),
                    OwnerDiscordIdBefore = table.Column<long>(type: "bigint", nullable: true),
                    OwnerDiscordIdAfter = table.Column<long>(type: "bigint", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GuildMembersChunkEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    MemberCount = table.Column<int>(type: "integer", nullable: false),
                    MemberIdsJson = table.Column<string>(type: "jsonb", nullable: true),
                    PresencesJson = table.Column<string>(type: "jsonb", nullable: true),
                    Nonce = table.Column<string>(type: "text", nullable: true),
                    NotFoundIdsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildMembersChunkEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildMembersChunkEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsSyncing = table.Column<bool>(type: "boolean", nullable: false),
                    RoleDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    ExpireBehavior = table.Column<int>(type: "integer", nullable: false),
                    ExpireGracePeriod = table.Column<int>(type: "integer", nullable: false),
                    ApplicationId = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Integrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Integrations_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Color = table.Column<int>(type: "integer", nullable: false),
                    IsHoisted = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Permissions = table.Column<long>(type: "bigint", nullable: false),
                    IsManaged = table.Column<bool>(type: "boolean", nullable: false),
                    IsMentionable = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Roles_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StickerEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    StickersAddedJson = table.Column<string>(type: "jsonb", nullable: true),
                    StickersRemovedJson = table.Column<string>(type: "jsonb", nullable: true),
                    StickersUpdatedJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StickerEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StickerEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Stickers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    PackId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    FormatType = table.Column<int>(type: "integer", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stickers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stickers_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ThreadSyncEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ThreadCount = table.Column<int>(type: "integer", nullable: false),
                    ThreadIdsJson = table.Column<string>(type: "jsonb", nullable: true),
                    ChannelIdsJson = table.Column<string>(type: "jsonb", nullable: true),
                    MembersJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreadSyncEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThreadSyncEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VoiceServerEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceServerEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoiceServerEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    ActivityType = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Details = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    State = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ApplicationId = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LargeImageUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LargeImageText = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SmallImageUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SmallImageText = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PartyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PartyCurrentSize = table.Column<int>(type: "integer", nullable: true),
                    PartyMaxSize = table.Column<int>(type: "integer", nullable: true),
                    SpotifyTrackId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SpotifyAlbumArtUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SpotifyAlbumTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SpotifyArtistsJson = table.Column<string>(type: "jsonb", nullable: true),
                    SpotifySongTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SpotifyTrackStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SpotifyTrackEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StreamUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CustomStatusEmojiId = table.Column<long>(type: "bigint", nullable: true),
                    CustomStatusEmojiName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ButtonsJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Activities_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Activities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuditLogEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuditLogDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    TargetDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    ActionType = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    ChangesJson = table.Column<string>(type: "jsonb", nullable: true),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AuditLogEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BanEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BanEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BanEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BanEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Bans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnbannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bans_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bans_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemberEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    NicknameBefore = table.Column<string>(type: "text", nullable: true),
                    NicknameAfter = table.Column<string>(type: "text", nullable: true),
                    RolesAddedJson = table.Column<string>(type: "jsonb", nullable: true),
                    RolesRemovedJson = table.Column<string>(type: "jsonb", nullable: true),
                    TimeoutUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BanReason = table.Column<string>(type: "text", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MemberEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nickname = table.Column<string>(type: "text", nullable: true),
                    GuildAvatarHash = table.Column<string>(type: "text", nullable: true),
                    JoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PremiumSinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeafened = table.Column<bool>(type: "boolean", nullable: false),
                    IsMuted = table.Column<bool>(type: "boolean", nullable: false),
                    IsPending = table.Column<bool>(type: "boolean", nullable: false),
                    TimeoutUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Members_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Members_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PresenceEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    DesktopStatusBefore = table.Column<int>(type: "integer", nullable: false),
                    MobileStatusBefore = table.Column<int>(type: "integer", nullable: false),
                    WebStatusBefore = table.Column<int>(type: "integer", nullable: false),
                    DesktopStatusAfter = table.Column<int>(type: "integer", nullable: false),
                    MobileStatusAfter = table.Column<int>(type: "integer", nullable: false),
                    WebStatusAfter = table.Column<int>(type: "integer", nullable: false),
                    ActivitiesBeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    ActivitiesAfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PresenceEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PresenceEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PresenceEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AutoModRuleEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RuleDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    CreatorDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    TriggerType = table.Column<int>(type: "integer", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    ActionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoModRuleEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutoModRuleEvents_AutoModRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "AutoModRules",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AutoModRuleEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AutoModRuleEvents_Users_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AutoModEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    RuleDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    RuleName = table.Column<string>(type: "text", nullable: true),
                    TriggerType = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    MessageDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    AlertSystemMessageDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    MatchedKeyword = table.Column<string>(type: "text", nullable: true),
                    MatchedContent = table.Column<string>(type: "text", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoModEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutoModEvents_AutoModRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "AutoModRules",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AutoModEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AutoModEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AutoModEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChannelEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelType = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    NameBefore = table.Column<string>(type: "text", nullable: true),
                    NameAfter = table.Column<string>(type: "text", nullable: true),
                    TopicBefore = table.Column<string>(type: "text", nullable: true),
                    TopicAfter = table.Column<string>(type: "text", nullable: true),
                    PositionBefore = table.Column<int>(type: "integer", nullable: true),
                    PositionAfter = table.Column<int>(type: "integer", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChannelEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GuildScheduledEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<int>(type: "integer", nullable: false),
                    ScheduledStartTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledEndTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EntityMetadataLocation = table.Column<string>(type: "text", nullable: true),
                    UserCount = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildScheduledEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildScheduledEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GuildScheduledEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GuildScheduledEvents_Users_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InviteEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    InviterId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    InviterDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    MaxAge = table.Column<int>(type: "integer", nullable: true),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    IsTemporary = table.Column<bool>(type: "boolean", nullable: false),
                    Uses = table.Column<int>(type: "integer", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InviteEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InviteEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InviteEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InviteEvents_Users_InviterId",
                        column: x => x.InviterId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Invites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    Code = table.Column<string>(type: "text", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    InviterId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaxAge = table.Column<int>(type: "integer", nullable: false),
                    MaxUses = table.Column<int>(type: "integer", nullable: false),
                    Uses = table.Column<int>(type: "integer", nullable: false),
                    IsTemporary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invites_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invites_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invites_Users_InviterId",
                        column: x => x.InviterId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    ReplyToDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    HasAttachments = table.Column<bool>(type: "boolean", nullable: false),
                    HasEmbeds = table.Column<bool>(type: "boolean", nullable: false),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EmbedsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EditedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Messages_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Messages_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PinEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    LastPinTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PinEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PinEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PinEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StageInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "text", nullable: false),
                    PrivacyLevel = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageInstances_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageInstances_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ThreadEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    ThreadId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    ThreadDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ParentChannelDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    MembersAddedJson = table.Column<string>(type: "jsonb", nullable: true),
                    MembersRemovedJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreadEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThreadEvents_Channels_ParentChannelId",
                        column: x => x.ParentChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ThreadEvents_Channels_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ThreadEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ThreadEvents_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TypingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TypingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TypingEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TypingEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TypingEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VoiceStateEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelIdBefore = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelIdAfter = table.Column<Guid>(type: "uuid", nullable: true),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelDiscordIdBefore = table.Column<long>(type: "bigint", nullable: true),
                    ChannelDiscordIdAfter = table.Column<long>(type: "bigint", nullable: true),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    WasSelfMuted = table.Column<bool>(type: "boolean", nullable: false),
                    WasSelfDeafened = table.Column<bool>(type: "boolean", nullable: false),
                    WasServerMuted = table.Column<bool>(type: "boolean", nullable: false),
                    WasServerDeafened = table.Column<bool>(type: "boolean", nullable: false),
                    WasStreaming = table.Column<bool>(type: "boolean", nullable: false),
                    WasVideo = table.Column<bool>(type: "boolean", nullable: false),
                    WasSuppressed = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelfMuted = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelfDeafened = table.Column<bool>(type: "boolean", nullable: false),
                    IsServerMuted = table.Column<bool>(type: "boolean", nullable: false),
                    IsServerDeafened = table.Column<bool>(type: "boolean", nullable: false),
                    IsStreaming = table.Column<bool>(type: "boolean", nullable: false),
                    IsVideo = table.Column<bool>(type: "boolean", nullable: false),
                    IsSuppressed = table.Column<bool>(type: "boolean", nullable: false),
                    SessionId = table.Column<string>(type: "text", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceStateEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoiceStateEvents_Channels_ChannelIdAfter",
                        column: x => x.ChannelIdAfter,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VoiceStateEvents_Channels_ChannelIdBefore",
                        column: x => x.ChannelIdBefore,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VoiceStateEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VoiceStateEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VoiceStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<string>(type: "text", nullable: true),
                    IsSelfMuted = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelfDeafened = table.Column<bool>(type: "boolean", nullable: false),
                    IsServerMuted = table.Column<bool>(type: "boolean", nullable: false),
                    IsServerDeafened = table.Column<bool>(type: "boolean", nullable: false),
                    IsStreaming = table.Column<bool>(type: "boolean", nullable: false),
                    IsVideo = table.Column<bool>(type: "boolean", nullable: false),
                    IsSuppressed = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedChannelAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoiceStates_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VoiceStates_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VoiceStates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WebhookEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Webhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: true),
                    AvatarHash = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Webhooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Webhooks_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Webhooks_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Webhooks_Users_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IntegrationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    IntegrationDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    ApplicationDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IntegrationEvents_Integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "Integrations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RoleEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoleDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    NameBefore = table.Column<string>(type: "text", nullable: true),
                    NameAfter = table.Column<string>(type: "text", nullable: true),
                    ColorBefore = table.Column<int>(type: "integer", nullable: true),
                    ColorAfter = table.Column<int>(type: "integer", nullable: true),
                    PermissionsBefore = table.Column<long>(type: "bigint", nullable: true),
                    PermissionsAfter = table.Column<long>(type: "bigint", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RoleEvents_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ScheduledEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    EventId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    CreatorDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    EntityType = table.Column<int>(type: "integer", nullable: true),
                    ScheduledStartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScheduledEndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_GuildScheduledEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "GuildScheduledEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_Users_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MessageEditHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ContentBefore = table.Column<string>(type: "text", nullable: true),
                    ContentAfter = table.Column<string>(type: "text", nullable: true),
                    EditedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageEditHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageEditHistory_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MessageEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    ContentBefore = table.Column<string>(type: "text", nullable: true),
                    HasAttachments = table.Column<bool>(type: "boolean", nullable: false),
                    HasEmbeds = table.Column<bool>(type: "boolean", nullable: false),
                    ReplyToMessageDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EmbedsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MessageEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MessageEvents_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MessageEvents_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PollEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    AnswerId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PollEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PollEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PollEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PollEvents_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PollEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReactionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    UserDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EmoteDiscordId = table.Column<long>(type: "bigint", nullable: true),
                    EmoteName = table.Column<string>(type: "text", nullable: false),
                    IsAnimated = table.Column<bool>(type: "boolean", nullable: false),
                    IsBurst = table.Column<bool>(type: "boolean", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReactionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReactionEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReactionEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReactionEvents_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReactionEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StageInstanceEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    StageInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    StageInstanceDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    GuildDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelDiscordId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    TopicBefore = table.Column<string>(type: "text", nullable: true),
                    TopicAfter = table.Column<string>(type: "text", nullable: true),
                    PrivacyLevelBefore = table.Column<int>(type: "integer", nullable: true),
                    PrivacyLevelAfter = table.Column<int>(type: "integer", nullable: true),
                    EventTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageInstanceEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageInstanceEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StageInstanceEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StageInstanceEvents_StageInstances_StageInstanceId",
                        column: x => x.StageInstanceId,
                        principalTable: "StageInstances",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_ActivityType",
                table: "Activities",
                column: "ActivityType");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_FirstSeenAtUtc",
                table: "Activities",
                column: "FirstSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_GuildId",
                table: "Activities",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_UserDiscordId",
                table: "Activities",
                column: "UserDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_UserDiscordId_IsActive",
                table: "Activities",
                columns: new[] { "UserDiscordId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_UserId",
                table: "Activities",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEvents_ActionType",
                table: "AuditLogEvents",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEvents_GuildDiscordId_EventTimestampUtc",
                table: "AuditLogEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEvents_GuildId",
                table: "AuditLogEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEvents_UserDiscordId",
                table: "AuditLogEvents",
                column: "UserDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEvents_UserId",
                table: "AuditLogEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoModEvents_ChannelId",
                table: "AutoModEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoModEvents_GuildDiscordId_EventTimestampUtc",
                table: "AutoModEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AutoModEvents_GuildId",
                table: "AutoModEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoModEvents_RuleDiscordId",
                table: "AutoModEvents",
                column: "RuleDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoModEvents_RuleId",
                table: "AutoModEvents",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoModEvents_UserId",
                table: "AutoModEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoModRuleEvents_CreatorId",
                table: "AutoModRuleEvents",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoModRuleEvents_GuildDiscordId_EventTimestampUtc",
                table: "AutoModRuleEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AutoModRuleEvents_GuildId",
                table: "AutoModRuleEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoModRuleEvents_RuleId",
                table: "AutoModRuleEvents",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoModRules_DiscordId",
                table: "AutoModRules",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutoModRules_GuildId",
                table: "AutoModRules",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_BanEvents_GuildDiscordId_EventTimestampUtc",
                table: "BanEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BanEvents_GuildId",
                table: "BanEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_BanEvents_UserDiscordId",
                table: "BanEvents",
                column: "UserDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_BanEvents_UserId",
                table: "BanEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Bans_GuildId",
                table: "Bans",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Bans_GuildId_IsActive",
                table: "Bans",
                columns: new[] { "GuildId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Bans_GuildId_UserId",
                table: "Bans",
                columns: new[] { "GuildId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bans_UserId",
                table: "Bans",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelEvents_ChannelDiscordId",
                table: "ChannelEvents",
                column: "ChannelDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelEvents_ChannelId",
                table: "ChannelEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelEvents_GuildDiscordId_EventTimestampUtc",
                table: "ChannelEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelEvents_GuildId",
                table: "ChannelEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_DiscordId",
                table: "Channels",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_GuildId",
                table: "Channels",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_EmojiEvents_GuildDiscordId_EventTimestampUtc",
                table: "EmojiEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmojiEvents_GuildId",
                table: "EmojiEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Emotes_DiscordId",
                table: "Emotes",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Emotes_GuildId",
                table: "Emotes",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildEvents_GuildDiscordId_EventTimestampUtc",
                table: "GuildEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GuildEvents_GuildId",
                table: "GuildEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildMembersChunkEvents_GuildDiscordId_EventTimestampUtc",
                table: "GuildMembersChunkEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GuildMembersChunkEvents_GuildId",
                table: "GuildMembersChunkEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_DiscordId",
                table: "Guilds",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuildScheduledEvents_ChannelId",
                table: "GuildScheduledEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildScheduledEvents_CreatorId",
                table: "GuildScheduledEvents",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildScheduledEvents_DiscordId",
                table: "GuildScheduledEvents",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuildScheduledEvents_GuildId",
                table: "GuildScheduledEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildScheduledEvents_GuildId_IsDeleted",
                table: "GuildScheduledEvents",
                columns: new[] { "GuildId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEvents_GuildDiscordId_EventTimestampUtc",
                table: "IntegrationEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEvents_GuildId",
                table: "IntegrationEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEvents_IntegrationId",
                table: "IntegrationEvents",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_DiscordId",
                table: "Integrations",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_GuildId",
                table: "Integrations",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_InviteEvents_ChannelId",
                table: "InviteEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_InviteEvents_GuildDiscordId_EventTimestampUtc",
                table: "InviteEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InviteEvents_GuildId",
                table: "InviteEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_InviteEvents_InviterId",
                table: "InviteEvents",
                column: "InviterId");

            migrationBuilder.CreateIndex(
                name: "IX_Invites_ChannelId",
                table: "Invites",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Invites_Code",
                table: "Invites",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invites_GuildId",
                table: "Invites",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Invites_GuildId_IsDeleted",
                table: "Invites",
                columns: new[] { "GuildId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Invites_InviterId",
                table: "Invites",
                column: "InviterId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberEvents_GuildDiscordId_EventTimestampUtc",
                table: "MemberEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MemberEvents_GuildId",
                table: "MemberEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberEvents_UserDiscordId",
                table: "MemberEvents",
                column: "UserDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberEvents_UserId",
                table: "MemberEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_GuildId",
                table: "Members",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_UserId_GuildId",
                table: "Members",
                columns: new[] { "UserId", "GuildId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageEditHistory_EditedAtUtc",
                table: "MessageEditHistory",
                column: "EditedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEditHistory_MessageDiscordId",
                table: "MessageEditHistory",
                column: "MessageDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEditHistory_MessageId",
                table: "MessageEditHistory",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEvents_AuthorDiscordId",
                table: "MessageEvents",
                column: "AuthorDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEvents_AuthorId",
                table: "MessageEvents",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEvents_ChannelDiscordId",
                table: "MessageEvents",
                column: "ChannelDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEvents_ChannelId",
                table: "MessageEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEvents_GuildDiscordId_EventTimestampUtc",
                table: "MessageEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageEvents_GuildId",
                table: "MessageEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEvents_MessageDiscordId",
                table: "MessageEvents",
                column: "MessageDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageEvents_MessageId",
                table: "MessageEvents",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_AuthorId",
                table: "Messages",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChannelId",
                table: "Messages",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChannelId_IsDeleted",
                table: "Messages",
                columns: new[] { "ChannelId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_DiscordId",
                table: "Messages",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_GuildId_CreatedAtUtc",
                table: "Messages",
                columns: new[] { "GuildId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PinEvents_ChannelDiscordId",
                table: "PinEvents",
                column: "ChannelDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_PinEvents_ChannelId",
                table: "PinEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_PinEvents_GuildDiscordId_EventTimestampUtc",
                table: "PinEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PinEvents_GuildId",
                table: "PinEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_PollEvents_ChannelId",
                table: "PollEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_PollEvents_GuildDiscordId_EventTimestampUtc",
                table: "PollEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PollEvents_GuildId",
                table: "PollEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_PollEvents_MessageDiscordId",
                table: "PollEvents",
                column: "MessageDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_PollEvents_MessageId",
                table: "PollEvents",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_PollEvents_UserId",
                table: "PollEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PresenceEvents_GuildDiscordId_EventTimestampUtc",
                table: "PresenceEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PresenceEvents_GuildId",
                table: "PresenceEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_PresenceEvents_UserDiscordId",
                table: "PresenceEvents",
                column: "UserDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_PresenceEvents_UserId",
                table: "PresenceEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RawEventLogs_EventType",
                table: "RawEventLogs",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_RawEventLogs_GuildDiscordId_ReceivedAtUtc",
                table: "RawEventLogs",
                columns: new[] { "GuildDiscordId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RawEventLogs_ReceivedAtUtc",
                table: "RawEventLogs",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RawEventLogs_UserDiscordId",
                table: "RawEventLogs",
                column: "UserDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionEvents_ChannelId",
                table: "ReactionEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionEvents_GuildDiscordId_EventTimestampUtc",
                table: "ReactionEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReactionEvents_GuildId",
                table: "ReactionEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionEvents_MessageDiscordId",
                table: "ReactionEvents",
                column: "MessageDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionEvents_MessageId",
                table: "ReactionEvents",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionEvents_UserId",
                table: "ReactionEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleEvents_GuildDiscordId_EventTimestampUtc",
                table: "RoleEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleEvents_GuildId",
                table: "RoleEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleEvents_RoleId",
                table: "RoleEvents",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_DiscordId",
                table: "Roles",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_GuildId",
                table: "Roles",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_ChannelId",
                table: "ScheduledEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_CreatorId",
                table: "ScheduledEvents",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_EventDiscordId",
                table: "ScheduledEvents",
                column: "EventDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_EventId",
                table: "ScheduledEvents",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_GuildDiscordId_EventTimestampUtc",
                table: "ScheduledEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_GuildId",
                table: "ScheduledEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_UserId",
                table: "ScheduledEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StageInstanceEvents_ChannelId",
                table: "StageInstanceEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_StageInstanceEvents_GuildDiscordId_EventTimestampUtc",
                table: "StageInstanceEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StageInstanceEvents_GuildId",
                table: "StageInstanceEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_StageInstanceEvents_StageInstanceId",
                table: "StageInstanceEvents",
                column: "StageInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_StageInstances_ChannelId",
                table: "StageInstances",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_StageInstances_DiscordId",
                table: "StageInstances",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageInstances_GuildId",
                table: "StageInstances",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_StickerEvents_GuildDiscordId_EventTimestampUtc",
                table: "StickerEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StickerEvents_GuildId",
                table: "StickerEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Stickers_DiscordId",
                table: "Stickers",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stickers_GuildId",
                table: "Stickers",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadEvents_GuildDiscordId_EventTimestampUtc",
                table: "ThreadEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ThreadEvents_GuildId",
                table: "ThreadEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadEvents_OwnerId",
                table: "ThreadEvents",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadEvents_ParentChannelDiscordId",
                table: "ThreadEvents",
                column: "ParentChannelDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadEvents_ParentChannelId",
                table: "ThreadEvents",
                column: "ParentChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadEvents_ThreadId",
                table: "ThreadEvents",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadSyncEvents_GuildDiscordId_EventTimestampUtc",
                table: "ThreadSyncEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ThreadSyncEvents_GuildId",
                table: "ThreadSyncEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_TypingEvents_ChannelId",
                table: "TypingEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_TypingEvents_GuildDiscordId_ReceivedAtUtc",
                table: "TypingEvents",
                columns: new[] { "GuildDiscordId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TypingEvents_GuildId",
                table: "TypingEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_TypingEvents_UserDiscordId",
                table: "TypingEvents",
                column: "UserDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_TypingEvents_UserId",
                table: "TypingEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DiscordId",
                table: "Users",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoiceServerEvents_GuildDiscordId_EventTimestampUtc",
                table: "VoiceServerEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceServerEvents_GuildId",
                table: "VoiceServerEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStateEvents_ChannelIdAfter",
                table: "VoiceStateEvents",
                column: "ChannelIdAfter");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStateEvents_ChannelIdBefore",
                table: "VoiceStateEvents",
                column: "ChannelIdBefore");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStateEvents_GuildDiscordId_EventTimestampUtc",
                table: "VoiceStateEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStateEvents_GuildId",
                table: "VoiceStateEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStateEvents_UserDiscordId",
                table: "VoiceStateEvents",
                column: "UserDiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStateEvents_UserId",
                table: "VoiceStateEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStates_ChannelId",
                table: "VoiceStates",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStates_GuildId",
                table: "VoiceStates",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStates_UserId_GuildId",
                table: "VoiceStates",
                columns: new[] { "UserId", "GuildId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_ChannelId",
                table: "WebhookEvents",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_GuildDiscordId_EventTimestampUtc",
                table: "WebhookEvents",
                columns: new[] { "GuildDiscordId", "EventTimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_GuildId",
                table: "WebhookEvents",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Webhooks_ChannelId",
                table: "Webhooks",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Webhooks_CreatorId",
                table: "Webhooks",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Webhooks_DiscordId",
                table: "Webhooks",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Webhooks_GuildId",
                table: "Webhooks",
                column: "GuildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "AuditLogEvents");

            migrationBuilder.DropTable(
                name: "AutoModEvents");

            migrationBuilder.DropTable(
                name: "AutoModRuleEvents");

            migrationBuilder.DropTable(
                name: "BanEvents");

            migrationBuilder.DropTable(
                name: "Bans");

            migrationBuilder.DropTable(
                name: "ChannelEvents");

            migrationBuilder.DropTable(
                name: "EmojiEvents");

            migrationBuilder.DropTable(
                name: "Emotes");

            migrationBuilder.DropTable(
                name: "GuildEvents");

            migrationBuilder.DropTable(
                name: "GuildMembersChunkEvents");

            migrationBuilder.DropTable(
                name: "IntegrationEvents");

            migrationBuilder.DropTable(
                name: "InviteEvents");

            migrationBuilder.DropTable(
                name: "Invites");

            migrationBuilder.DropTable(
                name: "MemberEvents");

            migrationBuilder.DropTable(
                name: "Members");

            migrationBuilder.DropTable(
                name: "MessageEditHistory");

            migrationBuilder.DropTable(
                name: "MessageEvents");

            migrationBuilder.DropTable(
                name: "PinEvents");

            migrationBuilder.DropTable(
                name: "PollEvents");

            migrationBuilder.DropTable(
                name: "PresenceEvents");

            migrationBuilder.DropTable(
                name: "RawEventLogs");

            migrationBuilder.DropTable(
                name: "ReactionEvents");

            migrationBuilder.DropTable(
                name: "RoleEvents");

            migrationBuilder.DropTable(
                name: "ScheduledEvents");

            migrationBuilder.DropTable(
                name: "StageInstanceEvents");

            migrationBuilder.DropTable(
                name: "StickerEvents");

            migrationBuilder.DropTable(
                name: "Stickers");

            migrationBuilder.DropTable(
                name: "ThreadEvents");

            migrationBuilder.DropTable(
                name: "ThreadSyncEvents");

            migrationBuilder.DropTable(
                name: "TypingEvents");

            migrationBuilder.DropTable(
                name: "VoiceServerEvents");

            migrationBuilder.DropTable(
                name: "VoiceStateEvents");

            migrationBuilder.DropTable(
                name: "VoiceStates");

            migrationBuilder.DropTable(
                name: "WebhookEvents");

            migrationBuilder.DropTable(
                name: "Webhooks");

            migrationBuilder.DropTable(
                name: "AutoModRules");

            migrationBuilder.DropTable(
                name: "Integrations");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "GuildScheduledEvents");

            migrationBuilder.DropTable(
                name: "StageInstances");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Channels");

            migrationBuilder.DropTable(
                name: "Guilds");
        }
    }
}
