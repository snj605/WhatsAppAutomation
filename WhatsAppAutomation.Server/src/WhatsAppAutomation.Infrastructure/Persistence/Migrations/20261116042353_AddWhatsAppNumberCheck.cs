using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppNumberCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "whatsapp_check",
                table: "contacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValueSql: "'unchecked'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "whatsapp_checked_at",
                table: "contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_user_id_whatsapp_check",
                table: "contacts",
                columns: new[] { "user_id", "whatsapp_check" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_contacts_user_id_whatsapp_check",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "whatsapp_check",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "whatsapp_checked_at",
                table: "contacts");
        }
    }
}
