using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P3_1_MemberRoleSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "member_role_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    granted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_role_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_member_role_snapshots_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_member_role_snapshots_member_id_role_discord_id",
                table: "member_role_snapshots",
                columns: new[] { "member_id", "role_discord_id" },
                unique: true,
                filter: "revoked_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_member_role_snapshots_member_id_role_discord_id_granted_at_",
                table: "member_role_snapshots",
                columns: new[] { "member_id", "role_discord_id", "granted_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "member_role_snapshots");
        }
    }
}
