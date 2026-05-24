using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P3_3_UserNameHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_name_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    username_before = table.Column<string>(type: "text", nullable: true),
                    username_after = table.Column<string>(type: "text", nullable: true),
                    global_name_before = table.Column<string>(type: "text", nullable: true),
                    global_name_after = table.Column<string>(type: "text", nullable: true),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_name_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_name_history_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_name_history_changed_at_utc",
                table: "user_name_history",
                column: "changed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_user_name_history_user_id",
                table: "user_name_history",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_name_history");
        }
    }
}
