using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModulesAndSendingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "chunk_rest_minutes",
                table: "user_preferences",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "chunk_size",
                table: "user_preferences",
                type: "integer",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "daily_limit",
                table: "user_preferences",
                type: "integer",
                nullable: false,
                defaultValue: 300);

            migrationBuilder.AddColumn<int>(
                name: "max_gap_seconds",
                table: "user_preferences",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<int>(
                name: "min_gap_seconds",
                table: "user_preferences",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "send_from",
                table: "user_preferences",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(9, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "send_until",
                table: "user_preferences",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(21, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "queue_position",
                table: "broadcasts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "broadcast_recipients",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "occasion_modules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    for_name_label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    auto_send = table.Column<bool>(type: "boolean", nullable: false),
                    send_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    auto_sent_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occasion_modules", x => x.id);
                    table.ForeignKey(
                        name: "fk_occasion_modules_message_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "message_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_occasion_modules_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_occasions",
                columns: table => new
                {
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    for_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_occasions", x => new { x.module_id, x.contact_id });
                    table.CheckConstraint("ck_contact_occasions_day", "day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_contact_occasions_month", "month BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "fk_contact_occasions_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contact_occasions_occasion_modules_module_id",
                        column: x => x.module_id,
                        principalTable: "occasion_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_broadcasts_user_id_status_queue_position",
                table: "broadcasts",
                columns: new[] { "user_id", "status", "queue_position" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_occasions_contact_id",
                table: "contact_occasions",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_occasions_module_id_month_day",
                table: "contact_occasions",
                columns: new[] { "module_id", "month", "day" });

            migrationBuilder.CreateIndex(
                name: "ix_occasion_modules_auto_send",
                table: "occasion_modules",
                column: "auto_send");

            migrationBuilder.CreateIndex(
                name: "ix_occasion_modules_template_id",
                table: "occasion_modules",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_occasion_modules_user_id_sort_order",
                table: "occasion_modules",
                columns: new[] { "user_id", "sort_order" });

            // Hand-written: keep every user's birthday wishes. Each user (not the platform admin) gets a Birthday
            // module with their saved time, template and "already sent today" day; earlier birthday wishes are
            // linked to it, and the existing broadcasts keep their order in the new queue.
            migrationBuilder.Sql("""
                INSERT INTO occasion_modules (id, user_id, name, kind, for_name_label, sort_order, auto_send, send_time, template_id, auto_sent_on, created_at)
                SELECT gen_random_uuid(), u.id, 'Birthday', 'Birthday', NULL, 1,
                       COALESCE(p.birthday_auto_send, false), p.birthday_send_time, p.birthday_template_id, p.birthday_auto_sent_on, now()
                FROM users u
                LEFT JOIN user_preferences p ON p.user_id = u.id
                WHERE u.kind <> 'platform_admin';

                UPDATE broadcasts b
                SET target_id = m.id
                FROM occasion_modules m
                WHERE b.target = 4 AND b.target_id IS NULL AND m.user_id = b.user_id AND m.kind = 'Birthday';

                UPDATE broadcasts b
                SET queue_position = q.position
                FROM (SELECT id, row_number() OVER (PARTITION BY user_id ORDER BY created_at) AS position FROM broadcasts) q
                WHERE b.id = q.id;
                """);

            // Last, once the birthday settings were copied above.
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_occasions");

            migrationBuilder.DropTable(
                name: "occasion_modules");

            migrationBuilder.DropIndex(
                name: "ix_broadcasts_user_id_status_queue_position",
                table: "broadcasts");

            migrationBuilder.DropColumn(
                name: "chunk_rest_minutes",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "chunk_size",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "daily_limit",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "max_gap_seconds",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "min_gap_seconds",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "send_from",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "send_until",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "queue_position",
                table: "broadcasts");

            migrationBuilder.DropColumn(
                name: "attempts",
                table: "broadcast_recipients");

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
    }
}
