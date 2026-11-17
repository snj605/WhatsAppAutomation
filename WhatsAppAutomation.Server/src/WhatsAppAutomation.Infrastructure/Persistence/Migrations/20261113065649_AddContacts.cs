using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_categories_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_groups_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_preferences",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_language = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_preferences", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    saved_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    name_hi = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    name_gu = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    language = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salutation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    birth_day = table.Column<int>(type: "integer", nullable: true),
                    birth_month = table.Column<int>(type: "integer", nullable: true),
                    birth_year = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contacts_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_contacts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_group_members",
                columns: table => new
                {
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_group_members", x => new { x.contact_id, x.group_id });
                    table.ForeignKey(
                        name: "fk_contact_group_members_contact_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "contact_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contact_group_members_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "name", "description", "group" },
                values: new object[,]
                {
                    { "contacts.manage", "Add, edit, import and delete your own contacts, categories and groups.", "Contacts" },
                    { "contacts.view", "See your own contacts, categories and groups.", "Contacts" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_categories_user_id",
                table: "categories",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_group_members_group_id",
                table: "contact_group_members",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_groups_user_id",
                table: "contact_groups",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_category_id",
                table: "contacts",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_user_id_phone_number",
                table: "contacts",
                columns: new[] { "user_id", "phone_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_group_members");

            migrationBuilder.DropTable(
                name: "user_preferences");

            migrationBuilder.DropTable(
                name: "contact_groups");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "contacts.manage");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "contacts.view");
        }
    }
}
