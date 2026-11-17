using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "whatsapp_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    profile_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whatsapp_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_whatsapp_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outgoing_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_phone_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    whatsapp_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_outgoing_messages_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_outgoing_messages_whats_app_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "whatsapp_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_auth_state",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    key_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whatsapp_auth_state", x => new { x.account_id, x.kind, x.key_id });
                    table.ForeignKey(
                        name: "fk_whatsapp_auth_state_whatsapp_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "whatsapp_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "name", "description", "group" },
                values: new object[,]
                {
                    { "messages.send", "Send messages from your linked WhatsApp number.", "Messages" },
                    { "whatsapp.link", "Link and unlink your own WhatsApp number.", "WhatsApp" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_messages_account_id_whatsapp_message_id",
                table: "outgoing_messages",
                columns: new[] { "account_id", "whatsapp_message_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_messages_user_id_created_at",
                table: "outgoing_messages",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_accounts_user_id",
                table: "whatsapp_accounts",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outgoing_messages");

            migrationBuilder.DropTable(
                name: "whatsapp_auth_state");

            migrationBuilder.DropTable(
                name: "whatsapp_accounts");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "messages.send");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "whatsapp.link");
        }
    }
}
