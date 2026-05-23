using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P2_4_MessageEditHistoryBroaden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_message_edit_history_messages_message_id",
                table: "message_edit_history");

            migrationBuilder.AddColumn<int>(
                name: "flags",
                table: "messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<Guid>(
                name: "message_id",
                table: "message_edit_history",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachments_after_json",
                table: "message_edit_history",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachments_before_json",
                table: "message_edit_history",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "embeds_after_json",
                table: "message_edit_history",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "embeds_before_json",
                table: "message_edit_history",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "flags_after",
                table: "message_edit_history",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "flags_before",
                table: "message_edit_history",
                type: "integer",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_message_edit_history_messages_message_id",
                table: "message_edit_history",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_message_edit_history_messages_message_id",
                table: "message_edit_history");

            migrationBuilder.DropColumn(
                name: "flags",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "attachments_after_json",
                table: "message_edit_history");

            migrationBuilder.DropColumn(
                name: "attachments_before_json",
                table: "message_edit_history");

            migrationBuilder.DropColumn(
                name: "embeds_after_json",
                table: "message_edit_history");

            migrationBuilder.DropColumn(
                name: "embeds_before_json",
                table: "message_edit_history");

            migrationBuilder.DropColumn(
                name: "flags_after",
                table: "message_edit_history");

            migrationBuilder.DropColumn(
                name: "flags_before",
                table: "message_edit_history");

            migrationBuilder.AlterColumn<Guid>(
                name: "message_id",
                table: "message_edit_history",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "fk_message_edit_history_messages_message_id",
                table: "message_edit_history",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "id");
        }
    }
}
