using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P2_3_DropDenormalizedFks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_audit_log_events_guilds_guild_id",
                table: "audit_log_events");

            migrationBuilder.DropForeignKey(
                name: "fk_audit_log_events_users_user_id",
                table: "audit_log_events");

            migrationBuilder.DropForeignKey(
                name: "fk_auto_mod_events_auto_mod_rules_rule_id",
                table: "auto_mod_events");

            migrationBuilder.DropForeignKey(
                name: "fk_auto_mod_events_channels_channel_id",
                table: "auto_mod_events");

            migrationBuilder.DropForeignKey(
                name: "fk_auto_mod_events_guilds_guild_id",
                table: "auto_mod_events");

            migrationBuilder.DropForeignKey(
                name: "fk_auto_mod_events_users_user_id",
                table: "auto_mod_events");

            migrationBuilder.DropForeignKey(
                name: "fk_auto_mod_rule_events_auto_mod_rules_rule_id",
                table: "auto_mod_rule_events");

            migrationBuilder.DropForeignKey(
                name: "fk_auto_mod_rule_events_guilds_guild_id",
                table: "auto_mod_rule_events");

            migrationBuilder.DropForeignKey(
                name: "fk_auto_mod_rule_events_users_creator_id",
                table: "auto_mod_rule_events");

            migrationBuilder.DropForeignKey(
                name: "fk_ban_events_guilds_guild_id",
                table: "ban_events");

            migrationBuilder.DropForeignKey(
                name: "fk_ban_events_users_user_id",
                table: "ban_events");

            migrationBuilder.DropForeignKey(
                name: "fk_channel_events_channels_channel_id",
                table: "channel_events");

            migrationBuilder.DropForeignKey(
                name: "fk_channel_events_guilds_guild_id",
                table: "channel_events");

            migrationBuilder.DropForeignKey(
                name: "fk_emoji_events_guilds_guild_id",
                table: "emoji_events");

            migrationBuilder.DropForeignKey(
                name: "fk_guild_events_guilds_guild_id",
                table: "guild_events");

            migrationBuilder.DropForeignKey(
                name: "fk_guild_members_chunk_events_guilds_guild_id",
                table: "guild_members_chunk_events");

            migrationBuilder.DropForeignKey(
                name: "fk_integration_events_guilds_guild_id",
                table: "integration_events");

            migrationBuilder.DropForeignKey(
                name: "fk_integration_events_integrations_integration_id",
                table: "integration_events");

            migrationBuilder.DropForeignKey(
                name: "fk_invite_events_channels_channel_id",
                table: "invite_events");

            migrationBuilder.DropForeignKey(
                name: "fk_invite_events_guilds_guild_id",
                table: "invite_events");

            migrationBuilder.DropForeignKey(
                name: "fk_invite_events_users_inviter_id",
                table: "invite_events");

            migrationBuilder.DropForeignKey(
                name: "fk_member_events_guilds_guild_id",
                table: "member_events");

            migrationBuilder.DropForeignKey(
                name: "fk_member_events_users_user_id",
                table: "member_events");

            migrationBuilder.DropForeignKey(
                name: "fk_message_events_channels_channel_id",
                table: "message_events");

            migrationBuilder.DropForeignKey(
                name: "fk_message_events_guilds_guild_id",
                table: "message_events");

            migrationBuilder.DropForeignKey(
                name: "fk_message_events_messages_message_id",
                table: "message_events");

            migrationBuilder.DropForeignKey(
                name: "fk_message_events_users_author_id",
                table: "message_events");

            migrationBuilder.DropForeignKey(
                name: "fk_pin_events_channels_channel_id",
                table: "pin_events");

            migrationBuilder.DropForeignKey(
                name: "fk_pin_events_guilds_guild_id",
                table: "pin_events");

            migrationBuilder.DropForeignKey(
                name: "fk_poll_events_channels_channel_id",
                table: "poll_events");

            migrationBuilder.DropForeignKey(
                name: "fk_poll_events_guilds_guild_id",
                table: "poll_events");

            migrationBuilder.DropForeignKey(
                name: "fk_poll_events_messages_message_id",
                table: "poll_events");

            migrationBuilder.DropForeignKey(
                name: "fk_poll_events_users_user_id",
                table: "poll_events");

            migrationBuilder.DropForeignKey(
                name: "fk_presence_events_guilds_guild_id",
                table: "presence_events");

            migrationBuilder.DropForeignKey(
                name: "fk_presence_events_users_user_id",
                table: "presence_events");

            migrationBuilder.DropForeignKey(
                name: "fk_reaction_events_channels_channel_id",
                table: "reaction_events");

            migrationBuilder.DropForeignKey(
                name: "fk_reaction_events_guilds_guild_id",
                table: "reaction_events");

            migrationBuilder.DropForeignKey(
                name: "fk_reaction_events_messages_message_id",
                table: "reaction_events");

            migrationBuilder.DropForeignKey(
                name: "fk_reaction_events_users_user_id",
                table: "reaction_events");

            migrationBuilder.DropForeignKey(
                name: "fk_role_events_guilds_guild_id",
                table: "role_events");

            migrationBuilder.DropForeignKey(
                name: "fk_role_events_roles_role_id",
                table: "role_events");

            migrationBuilder.DropForeignKey(
                name: "fk_scheduled_events_channels_channel_id",
                table: "scheduled_events");

            migrationBuilder.DropForeignKey(
                name: "fk_scheduled_events_guild_scheduled_events_event_id",
                table: "scheduled_events");

            migrationBuilder.DropForeignKey(
                name: "fk_scheduled_events_guilds_guild_id",
                table: "scheduled_events");

            migrationBuilder.DropForeignKey(
                name: "fk_scheduled_events_users_creator_id",
                table: "scheduled_events");

            migrationBuilder.DropForeignKey(
                name: "fk_scheduled_events_users_user_id",
                table: "scheduled_events");

            migrationBuilder.DropForeignKey(
                name: "fk_stage_instance_events_channels_channel_id",
                table: "stage_instance_events");

            migrationBuilder.DropForeignKey(
                name: "fk_stage_instance_events_guilds_guild_id",
                table: "stage_instance_events");

            migrationBuilder.DropForeignKey(
                name: "fk_stage_instance_events_stage_instances_stage_instance_id",
                table: "stage_instance_events");

            migrationBuilder.DropForeignKey(
                name: "fk_sticker_events_guilds_guild_id",
                table: "sticker_events");

            migrationBuilder.DropForeignKey(
                name: "fk_thread_events_channels_parent_channel_id",
                table: "thread_events");

            migrationBuilder.DropForeignKey(
                name: "fk_thread_events_channels_thread_id",
                table: "thread_events");

            migrationBuilder.DropForeignKey(
                name: "fk_thread_events_guilds_guild_id",
                table: "thread_events");

            migrationBuilder.DropForeignKey(
                name: "fk_thread_events_users_owner_id",
                table: "thread_events");

            migrationBuilder.DropForeignKey(
                name: "fk_thread_sync_events_guilds_guild_id",
                table: "thread_sync_events");

            migrationBuilder.DropForeignKey(
                name: "fk_typing_events_channels_channel_id",
                table: "typing_events");

            migrationBuilder.DropForeignKey(
                name: "fk_typing_events_guilds_guild_id",
                table: "typing_events");

            migrationBuilder.DropForeignKey(
                name: "fk_typing_events_users_user_id",
                table: "typing_events");

            migrationBuilder.DropForeignKey(
                name: "fk_voice_server_events_guilds_guild_id",
                table: "voice_server_events");

            migrationBuilder.DropForeignKey(
                name: "fk_voice_state_events_channels_channel_id_after",
                table: "voice_state_events");

            migrationBuilder.DropForeignKey(
                name: "fk_voice_state_events_channels_channel_id_before",
                table: "voice_state_events");

            migrationBuilder.DropForeignKey(
                name: "fk_voice_state_events_guilds_guild_id",
                table: "voice_state_events");

            migrationBuilder.DropForeignKey(
                name: "fk_voice_state_events_users_user_id",
                table: "voice_state_events");

            migrationBuilder.DropForeignKey(
                name: "fk_webhook_events_channels_channel_id",
                table: "webhook_events");

            migrationBuilder.DropForeignKey(
                name: "fk_webhook_events_guilds_guild_id",
                table: "webhook_events");

            migrationBuilder.DropIndex(
                name: "ix_webhook_events_channel_id",
                table: "webhook_events");

            migrationBuilder.DropIndex(
                name: "ix_webhook_events_guild_id",
                table: "webhook_events");

            migrationBuilder.DropIndex(
                name: "ix_voice_state_events_channel_id_after",
                table: "voice_state_events");

            migrationBuilder.DropIndex(
                name: "ix_voice_state_events_channel_id_before",
                table: "voice_state_events");

            migrationBuilder.DropIndex(
                name: "ix_voice_state_events_guild_id",
                table: "voice_state_events");

            migrationBuilder.DropIndex(
                name: "ix_voice_state_events_user_id",
                table: "voice_state_events");

            migrationBuilder.DropIndex(
                name: "ix_voice_server_events_guild_id",
                table: "voice_server_events");

            migrationBuilder.DropIndex(
                name: "ix_typing_events_channel_id",
                table: "typing_events");

            migrationBuilder.DropIndex(
                name: "ix_typing_events_guild_id",
                table: "typing_events");

            migrationBuilder.DropIndex(
                name: "ix_typing_events_user_id",
                table: "typing_events");

            migrationBuilder.DropIndex(
                name: "ix_thread_sync_events_guild_id",
                table: "thread_sync_events");

            migrationBuilder.DropIndex(
                name: "ix_thread_events_guild_id",
                table: "thread_events");

            migrationBuilder.DropIndex(
                name: "ix_thread_events_owner_id",
                table: "thread_events");

            migrationBuilder.DropIndex(
                name: "ix_thread_events_parent_channel_id",
                table: "thread_events");

            migrationBuilder.DropIndex(
                name: "ix_thread_events_thread_id",
                table: "thread_events");

            migrationBuilder.DropIndex(
                name: "ix_sticker_events_guild_id",
                table: "sticker_events");

            migrationBuilder.DropIndex(
                name: "ix_stage_instance_events_channel_id",
                table: "stage_instance_events");

            migrationBuilder.DropIndex(
                name: "ix_stage_instance_events_guild_id",
                table: "stage_instance_events");

            migrationBuilder.DropIndex(
                name: "ix_stage_instance_events_stage_instance_id",
                table: "stage_instance_events");

            migrationBuilder.DropIndex(
                name: "ix_scheduled_events_channel_id",
                table: "scheduled_events");

            migrationBuilder.DropIndex(
                name: "ix_scheduled_events_creator_id",
                table: "scheduled_events");

            migrationBuilder.DropIndex(
                name: "ix_scheduled_events_event_id",
                table: "scheduled_events");

            migrationBuilder.DropIndex(
                name: "ix_scheduled_events_guild_id",
                table: "scheduled_events");

            migrationBuilder.DropIndex(
                name: "ix_scheduled_events_user_id",
                table: "scheduled_events");

            migrationBuilder.DropIndex(
                name: "ix_role_events_guild_id",
                table: "role_events");

            migrationBuilder.DropIndex(
                name: "ix_role_events_role_id",
                table: "role_events");

            migrationBuilder.DropIndex(
                name: "ix_reaction_events_channel_id",
                table: "reaction_events");

            migrationBuilder.DropIndex(
                name: "ix_reaction_events_guild_id",
                table: "reaction_events");

            migrationBuilder.DropIndex(
                name: "ix_reaction_events_message_id",
                table: "reaction_events");

            migrationBuilder.DropIndex(
                name: "ix_reaction_events_user_id",
                table: "reaction_events");

            migrationBuilder.DropIndex(
                name: "ix_presence_events_guild_id",
                table: "presence_events");

            migrationBuilder.DropIndex(
                name: "ix_presence_events_user_id",
                table: "presence_events");

            migrationBuilder.DropIndex(
                name: "ix_poll_events_channel_id",
                table: "poll_events");

            migrationBuilder.DropIndex(
                name: "ix_poll_events_guild_id",
                table: "poll_events");

            migrationBuilder.DropIndex(
                name: "ix_poll_events_message_id",
                table: "poll_events");

            migrationBuilder.DropIndex(
                name: "ix_poll_events_user_id",
                table: "poll_events");

            migrationBuilder.DropIndex(
                name: "ix_pin_events_channel_id",
                table: "pin_events");

            migrationBuilder.DropIndex(
                name: "ix_pin_events_guild_id",
                table: "pin_events");

            migrationBuilder.DropIndex(
                name: "ix_message_events_author_id",
                table: "message_events");

            migrationBuilder.DropIndex(
                name: "ix_message_events_channel_id",
                table: "message_events");

            migrationBuilder.DropIndex(
                name: "ix_message_events_guild_id",
                table: "message_events");

            migrationBuilder.DropIndex(
                name: "ix_message_events_message_id",
                table: "message_events");

            migrationBuilder.DropIndex(
                name: "ix_member_events_guild_id",
                table: "member_events");

            migrationBuilder.DropIndex(
                name: "ix_member_events_user_id",
                table: "member_events");

            migrationBuilder.DropIndex(
                name: "ix_invite_events_channel_id",
                table: "invite_events");

            migrationBuilder.DropIndex(
                name: "ix_invite_events_guild_id",
                table: "invite_events");

            migrationBuilder.DropIndex(
                name: "ix_invite_events_inviter_id",
                table: "invite_events");

            migrationBuilder.DropIndex(
                name: "ix_integration_events_guild_id",
                table: "integration_events");

            migrationBuilder.DropIndex(
                name: "ix_integration_events_integration_id",
                table: "integration_events");

            migrationBuilder.DropIndex(
                name: "ix_guild_members_chunk_events_guild_id",
                table: "guild_members_chunk_events");

            migrationBuilder.DropIndex(
                name: "ix_guild_events_guild_id",
                table: "guild_events");

            migrationBuilder.DropIndex(
                name: "ix_emoji_events_guild_id",
                table: "emoji_events");

            migrationBuilder.DropIndex(
                name: "ix_channel_events_channel_id",
                table: "channel_events");

            migrationBuilder.DropIndex(
                name: "ix_channel_events_guild_id",
                table: "channel_events");

            migrationBuilder.DropIndex(
                name: "ix_ban_events_guild_id",
                table: "ban_events");

            migrationBuilder.DropIndex(
                name: "ix_ban_events_user_id",
                table: "ban_events");

            migrationBuilder.DropIndex(
                name: "ix_auto_mod_rule_events_creator_id",
                table: "auto_mod_rule_events");

            migrationBuilder.DropIndex(
                name: "ix_auto_mod_rule_events_guild_id",
                table: "auto_mod_rule_events");

            migrationBuilder.DropIndex(
                name: "ix_auto_mod_rule_events_rule_id",
                table: "auto_mod_rule_events");

            migrationBuilder.DropIndex(
                name: "ix_auto_mod_events_channel_id",
                table: "auto_mod_events");

            migrationBuilder.DropIndex(
                name: "ix_auto_mod_events_guild_id",
                table: "auto_mod_events");

            migrationBuilder.DropIndex(
                name: "ix_auto_mod_events_rule_id",
                table: "auto_mod_events");

            migrationBuilder.DropIndex(
                name: "ix_auto_mod_events_user_id",
                table: "auto_mod_events");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_events_guild_id",
                table: "audit_log_events");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_events_user_id",
                table: "audit_log_events");

            migrationBuilder.DropIndex(
                name: "ix_activities_user_discord_id",
                table: "activities");

            migrationBuilder.DropIndex(
                name: "ix_activities_user_discord_id_is_active",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "channel_id_after",
                table: "voice_state_events");

            migrationBuilder.DropColumn(
                name: "channel_id_before",
                table: "voice_state_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "voice_state_events");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "voice_state_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "voice_server_events");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "typing_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "typing_events");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "typing_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "thread_sync_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "thread_events");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "thread_events");

            migrationBuilder.DropColumn(
                name: "parent_channel_id",
                table: "thread_events");

            migrationBuilder.DropColumn(
                name: "thread_id",
                table: "thread_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "sticker_events");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "stage_instance_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "stage_instance_events");

            migrationBuilder.DropColumn(
                name: "stage_instance_id",
                table: "stage_instance_events");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "scheduled_events");

            migrationBuilder.DropColumn(
                name: "creator_id",
                table: "scheduled_events");

            migrationBuilder.DropColumn(
                name: "event_id",
                table: "scheduled_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "scheduled_events");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "scheduled_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "role_events");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "role_events");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "reaction_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "reaction_events");

            migrationBuilder.DropColumn(
                name: "message_id",
                table: "reaction_events");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "reaction_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "presence_events");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "presence_events");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "poll_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "poll_events");

            migrationBuilder.DropColumn(
                name: "message_id",
                table: "poll_events");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "poll_events");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "pin_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "pin_events");

            migrationBuilder.DropColumn(
                name: "author_id",
                table: "message_events");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "message_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "message_events");

            migrationBuilder.DropColumn(
                name: "message_id",
                table: "message_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "invite_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "invite_events");

            migrationBuilder.DropColumn(
                name: "inviter_id",
                table: "invite_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "integration_events");

            migrationBuilder.DropColumn(
                name: "integration_id",
                table: "integration_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "guild_members_chunk_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "guild_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "emoji_events");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "channel_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "channel_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "ban_events");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "ban_events");

            migrationBuilder.DropColumn(
                name: "creator_id",
                table: "auto_mod_rule_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "auto_mod_rule_events");

            migrationBuilder.DropColumn(
                name: "rule_id",
                table: "auto_mod_rule_events");

            migrationBuilder.DropColumn(
                name: "channel_id",
                table: "auto_mod_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "auto_mod_events");

            migrationBuilder.DropColumn(
                name: "message_id",
                table: "auto_mod_events");

            migrationBuilder.DropColumn(
                name: "rule_id",
                table: "auto_mod_events");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "auto_mod_events");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "audit_log_events");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "audit_log_events");

            migrationBuilder.DropColumn(
                name: "guild_discord_id",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "user_discord_id",
                table: "activities");

            migrationBuilder.CreateIndex(
                name: "ix_activities_user_id_is_active",
                table: "activities",
                columns: new[] { "user_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_activities_user_id_is_active",
                table: "activities");

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "webhook_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "webhook_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id_after",
                table: "voice_state_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id_before",
                table: "voice_state_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "voice_state_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "voice_state_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "voice_server_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "typing_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "typing_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "typing_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "thread_sync_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "thread_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "thread_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_channel_id",
                table: "thread_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "thread_id",
                table: "thread_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "sticker_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "stage_instance_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "stage_instance_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "stage_instance_id",
                table: "stage_instance_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "scheduled_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "creator_id",
                table: "scheduled_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                table: "scheduled_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "scheduled_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "scheduled_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "role_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "role_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "reaction_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "reaction_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                table: "reaction_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "reaction_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "presence_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "presence_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "poll_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "poll_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                table: "poll_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "poll_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "pin_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "pin_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "author_id",
                table: "message_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "message_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "message_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                table: "message_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "member_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "member_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "invite_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "invite_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "inviter_id",
                table: "invite_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "integration_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "integration_id",
                table: "integration_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "guild_members_chunk_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "guild_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "emoji_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "channel_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "channel_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "ban_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "ban_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "creator_id",
                table: "auto_mod_rule_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "auto_mod_rule_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rule_id",
                table: "auto_mod_rule_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_id",
                table: "auto_mod_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "auto_mod_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                table: "auto_mod_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rule_id",
                table: "auto_mod_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "auto_mod_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "audit_log_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "audit_log_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "guild_discord_id",
                table: "activities",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "user_discord_id",
                table: "activities",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_channel_id",
                table: "webhook_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_guild_id",
                table: "webhook_events",
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
                name: "ix_voice_state_events_guild_id",
                table: "voice_state_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_state_events_user_id",
                table: "voice_state_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_server_events_guild_id",
                table: "voice_server_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_typing_events_channel_id",
                table: "typing_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_typing_events_guild_id",
                table: "typing_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_typing_events_user_id",
                table: "typing_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_sync_events_guild_id",
                table: "thread_sync_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_events_guild_id",
                table: "thread_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_events_owner_id",
                table: "thread_events",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_events_parent_channel_id",
                table: "thread_events",
                column: "parent_channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_thread_events_thread_id",
                table: "thread_events",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "ix_sticker_events_guild_id",
                table: "sticker_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_instance_events_channel_id",
                table: "stage_instance_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_instance_events_guild_id",
                table: "stage_instance_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_instance_events_stage_instance_id",
                table: "stage_instance_events",
                column: "stage_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_channel_id",
                table: "scheduled_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_creator_id",
                table: "scheduled_events",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_event_id",
                table: "scheduled_events",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_guild_id",
                table: "scheduled_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_events_user_id",
                table: "scheduled_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_events_guild_id",
                table: "role_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_events_role_id",
                table: "role_events",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_events_channel_id",
                table: "reaction_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_events_guild_id",
                table: "reaction_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_events_message_id",
                table: "reaction_events",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_events_user_id",
                table: "reaction_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_presence_events_guild_id",
                table: "presence_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_presence_events_user_id",
                table: "presence_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_events_channel_id",
                table: "poll_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_events_guild_id",
                table: "poll_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_events_message_id",
                table: "poll_events",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_events_user_id",
                table: "poll_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pin_events_channel_id",
                table: "pin_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_pin_events_guild_id",
                table: "pin_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_author_id",
                table: "message_events",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_channel_id",
                table: "message_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_guild_id",
                table: "message_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_events_message_id",
                table: "message_events",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_events_guild_id",
                table: "member_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_events_user_id",
                table: "member_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invite_events_channel_id",
                table: "invite_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_invite_events_guild_id",
                table: "invite_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_invite_events_inviter_id",
                table: "invite_events",
                column: "inviter_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_guild_id",
                table: "integration_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_integration_id",
                table: "integration_events",
                column: "integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_members_chunk_events_guild_id",
                table: "guild_members_chunk_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_events_guild_id",
                table: "guild_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_emoji_events_guild_id",
                table: "emoji_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_events_channel_id",
                table: "channel_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_events_guild_id",
                table: "channel_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_ban_events_guild_id",
                table: "ban_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_ban_events_user_id",
                table: "ban_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rule_events_creator_id",
                table: "auto_mod_rule_events",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rule_events_guild_id",
                table: "auto_mod_rule_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rule_events_rule_id",
                table: "auto_mod_rule_events",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_events_channel_id",
                table: "auto_mod_events",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_events_guild_id",
                table: "auto_mod_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_events_rule_id",
                table: "auto_mod_events",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_events_user_id",
                table: "auto_mod_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_events_guild_id",
                table: "audit_log_events",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_events_user_id",
                table: "audit_log_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activities_user_discord_id",
                table: "activities",
                column: "user_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_activities_user_discord_id_is_active",
                table: "activities",
                columns: new[] { "user_discord_id", "is_active" });

            migrationBuilder.AddForeignKey(
                name: "fk_audit_log_events_guilds_guild_id",
                table: "audit_log_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_audit_log_events_users_user_id",
                table: "audit_log_events",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_mod_events_auto_mod_rules_rule_id",
                table: "auto_mod_events",
                column: "rule_id",
                principalTable: "auto_mod_rules",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_mod_events_channels_channel_id",
                table: "auto_mod_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_mod_events_guilds_guild_id",
                table: "auto_mod_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_mod_events_users_user_id",
                table: "auto_mod_events",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_mod_rule_events_auto_mod_rules_rule_id",
                table: "auto_mod_rule_events",
                column: "rule_id",
                principalTable: "auto_mod_rules",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_mod_rule_events_guilds_guild_id",
                table: "auto_mod_rule_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_mod_rule_events_users_creator_id",
                table: "auto_mod_rule_events",
                column: "creator_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_ban_events_guilds_guild_id",
                table: "ban_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_ban_events_users_user_id",
                table: "ban_events",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_channel_events_channels_channel_id",
                table: "channel_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_channel_events_guilds_guild_id",
                table: "channel_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_emoji_events_guilds_guild_id",
                table: "emoji_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_guild_events_guilds_guild_id",
                table: "guild_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_guild_members_chunk_events_guilds_guild_id",
                table: "guild_members_chunk_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_integration_events_guilds_guild_id",
                table: "integration_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_integration_events_integrations_integration_id",
                table: "integration_events",
                column: "integration_id",
                principalTable: "integrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invite_events_channels_channel_id",
                table: "invite_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invite_events_guilds_guild_id",
                table: "invite_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invite_events_users_inviter_id",
                table: "invite_events",
                column: "inviter_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_member_events_guilds_guild_id",
                table: "member_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_member_events_users_user_id",
                table: "member_events",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_message_events_channels_channel_id",
                table: "message_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_message_events_guilds_guild_id",
                table: "message_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_message_events_messages_message_id",
                table: "message_events",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_message_events_users_author_id",
                table: "message_events",
                column: "author_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_pin_events_channels_channel_id",
                table: "pin_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_pin_events_guilds_guild_id",
                table: "pin_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_poll_events_channels_channel_id",
                table: "poll_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_poll_events_guilds_guild_id",
                table: "poll_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_poll_events_messages_message_id",
                table: "poll_events",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_poll_events_users_user_id",
                table: "poll_events",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_presence_events_guilds_guild_id",
                table: "presence_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_presence_events_users_user_id",
                table: "presence_events",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_reaction_events_channels_channel_id",
                table: "reaction_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_reaction_events_guilds_guild_id",
                table: "reaction_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_reaction_events_messages_message_id",
                table: "reaction_events",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_reaction_events_users_user_id",
                table: "reaction_events",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_role_events_guilds_guild_id",
                table: "role_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_role_events_roles_role_id",
                table: "role_events",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_scheduled_events_channels_channel_id",
                table: "scheduled_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_scheduled_events_guild_scheduled_events_event_id",
                table: "scheduled_events",
                column: "event_id",
                principalTable: "guild_scheduled_events",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_scheduled_events_guilds_guild_id",
                table: "scheduled_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_scheduled_events_users_creator_id",
                table: "scheduled_events",
                column: "creator_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_scheduled_events_users_user_id",
                table: "scheduled_events",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_stage_instance_events_channels_channel_id",
                table: "stage_instance_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_stage_instance_events_guilds_guild_id",
                table: "stage_instance_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_stage_instance_events_stage_instances_stage_instance_id",
                table: "stage_instance_events",
                column: "stage_instance_id",
                principalTable: "stage_instances",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sticker_events_guilds_guild_id",
                table: "sticker_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_thread_events_channels_parent_channel_id",
                table: "thread_events",
                column: "parent_channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_thread_events_channels_thread_id",
                table: "thread_events",
                column: "thread_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_thread_events_guilds_guild_id",
                table: "thread_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_thread_events_users_owner_id",
                table: "thread_events",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_thread_sync_events_guilds_guild_id",
                table: "thread_sync_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_typing_events_channels_channel_id",
                table: "typing_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_typing_events_guilds_guild_id",
                table: "typing_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_typing_events_users_user_id",
                table: "typing_events",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_voice_server_events_guilds_guild_id",
                table: "voice_server_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_voice_state_events_channels_channel_id_after",
                table: "voice_state_events",
                column: "channel_id_after",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_voice_state_events_channels_channel_id_before",
                table: "voice_state_events",
                column: "channel_id_before",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_voice_state_events_guilds_guild_id",
                table: "voice_state_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_voice_state_events_users_user_id",
                table: "voice_state_events",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_webhook_events_channels_channel_id",
                table: "webhook_events",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_webhook_events_guilds_guild_id",
                table: "webhook_events",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");
        }
    }
}
