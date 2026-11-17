using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOptOutAndSendingPace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "opted_out_at",
                table: "contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sending_paces",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_send_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_since_rest = table.Column<int>(type: "integer", nullable: false),
                    failures_in_row = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sending_paces", x => x.account_id);
                    table.ForeignKey(
                        name: "fk_sending_paces_whats_app_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "whatsapp_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sending_paces");

            migrationBuilder.DropColumn(
                name: "opted_out_at",
                table: "contacts");
        }
    }
}
