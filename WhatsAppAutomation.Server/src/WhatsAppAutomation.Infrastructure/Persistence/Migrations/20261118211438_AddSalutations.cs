using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalutations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "salutation_id",
                table: "contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "salutations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name_gu = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salutations", x => x.id);
                    table.ForeignKey(
                        name: "fk_salutations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_salutation_id",
                table: "contacts",
                column: "salutation_id");

            migrationBuilder.CreateIndex(
                name: "ix_salutations_user_id_name_en",
                table: "salutations",
                columns: new[] { "user_id", "name_en" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salutations_user_id_sort_order",
                table: "salutations",
                columns: new[] { "user_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "fk_contacts_salutations_salutation_id",
                table: "contacts",
                column: "salutation_id",
                principalTable: "salutations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Keep every salutation already typed on contacts: each user's distinct ones become list entries (the same
            // text in both languages, to be corrected in the app), and their contacts point to them. Nothing is removed.
            migrationBuilder.Sql("""
                INSERT INTO salutations (id, user_id, name_en, name_gu, sort_order, created_at)
                SELECT gen_random_uuid(), s.user_id, s.text, s.text,
                       row_number() OVER (PARTITION BY s.user_id ORDER BY s.text), now()
                FROM (
                    SELECT c.user_id, min(left(btrim(c.salutation), 30)) AS text
                    FROM contacts c
                    WHERE c.salutation IS NOT NULL AND btrim(c.salutation) <> ''
                    GROUP BY c.user_id, lower(left(btrim(c.salutation), 30))
                ) s;

                UPDATE contacts c
                SET salutation_id = s.id
                FROM salutations s
                WHERE s.user_id = c.user_id
                  AND c.salutation IS NOT NULL
                  AND lower(left(btrim(c.salutation), 30)) = lower(s.name_en);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_contacts_salutations_salutation_id",
                table: "contacts");

            migrationBuilder.DropTable(
                name: "salutations");

            migrationBuilder.DropIndex(
                name: "ix_contacts_salutation_id",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "salutation_id",
                table: "contacts");
        }
    }
}
