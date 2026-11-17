using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBroadcasts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broadcasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text_en = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    text_hi = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    text_gu = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pause_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broadcasts", x => x.id);
                    table.ForeignKey(
                        name: "fk_broadcasts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "broadcast_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    broadcast_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    phone_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    language = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    outgoing_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broadcast_recipients", x => x.id);
                    table.ForeignKey(
                        name: "fk_broadcast_recipients_broadcasts_broadcast_id",
                        column: x => x.broadcast_id,
                        principalTable: "broadcasts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_broadcast_recipients_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_broadcast_recipients_outgoing_messages_outgoing_message_id",
                        column: x => x.outgoing_message_id,
                        principalTable: "outgoing_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "name", "description", "group" },
                values: new object[] { "broadcasts.send", "Create, start, pause and cancel your own broadcasts, and see their progress.", "Broadcasts" });

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_recipients_broadcast_id_status_order",
                table: "broadcast_recipients",
                columns: new[] { "broadcast_id", "status", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_recipients_contact_id",
                table: "broadcast_recipients",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_recipients_outgoing_message_id",
                table: "broadcast_recipients",
                column: "outgoing_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_recipients_phone_number_processed_at",
                table: "broadcast_recipients",
                columns: new[] { "phone_number", "processed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_broadcasts_status",
                table: "broadcasts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_broadcasts_user_id_created_at",
                table: "broadcasts",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broadcast_recipients");

            migrationBuilder.DropTable(
                name: "broadcasts");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "broadcasts.send");
        }
    }
}
