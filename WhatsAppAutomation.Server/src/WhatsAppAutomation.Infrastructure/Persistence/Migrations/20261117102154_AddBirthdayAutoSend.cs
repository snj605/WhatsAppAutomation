using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBirthdayAutoSend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "birthday_auto_send",
                table: "user_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "birthday_auto_sent_on",
                table: "user_preferences",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "birthday_send_time",
                table: "user_preferences",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "birthday_template_id",
                table: "user_preferences",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_preferences_birthday_auto_send",
                table: "user_preferences",
                column: "birthday_auto_send");

            migrationBuilder.CreateIndex(
                name: "ix_user_preferences_birthday_template_id",
                table: "user_preferences",
                column: "birthday_template_id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_preferences_message_templates_birthday_template_id",
                table: "user_preferences",
                column: "birthday_template_id",
                principalTable: "message_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_preferences_message_templates_birthday_template_id",
                table: "user_preferences");

            migrationBuilder.DropIndex(
                name: "ix_user_preferences_birthday_auto_send",
                table: "user_preferences");

            migrationBuilder.DropIndex(
                name: "ix_user_preferences_birthday_template_id",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "birthday_auto_send",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "birthday_auto_sent_on",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "birthday_send_time",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "birthday_template_id",
                table: "user_preferences");
        }
    }
}
