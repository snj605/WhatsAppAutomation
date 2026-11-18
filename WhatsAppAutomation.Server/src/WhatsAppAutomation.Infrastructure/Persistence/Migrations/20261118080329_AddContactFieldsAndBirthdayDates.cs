using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactFieldsAndBirthdayDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    options = table.Column<List<string>>(type: "text[]", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_fields", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_fields_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_field_values",
                columns: table => new
                {
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_field_values", x => new { x.field_id, x.contact_id });
                    table.ForeignKey(
                        name: "fk_contact_field_values_contact_fields_field_id",
                        column: x => x.field_id,
                        principalTable: "contact_fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contact_field_values_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_field_values_contact_id",
                table: "contact_field_values",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_field_values_field_id_value",
                table: "contact_field_values",
                columns: new[] { "field_id", "value" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_fields_user_id_key",
                table: "contact_fields",
                columns: new[] { "user_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contact_fields_user_id_sort_order",
                table: "contact_fields",
                columns: new[] { "user_id", "sort_order" });

            // Birthdays become dates of each user's Birthday occasion, like every other occasion. The birthday also
            // stays on the contact (nothing is removed), for users without a Birthday occasion.
            migrationBuilder.Sql("""
                INSERT INTO contact_occasions (module_id, contact_id, day, month, year, for_name)
                SELECT m.id, c.id, c.birth_day, c.birth_month, c.birth_year, NULL
                FROM contacts c
                JOIN occasion_modules m ON m.user_id = c.user_id AND m.kind = 'Birthday'
                WHERE c.birth_day BETWEEN 1 AND 31 AND c.birth_month BETWEEN 1 AND 12
                ON CONFLICT (module_id, contact_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_field_values");

            migrationBuilder.DropTable(
                name: "contact_fields");
        }
    }
}
