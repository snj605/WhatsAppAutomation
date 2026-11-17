using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValueSql: "'member'");

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_switches",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_switches", x => new { x.user_id, x.permission });
                    table.ForeignKey(
                        name: "fk_user_switches_permissions_permission",
                        column: x => x.permission,
                        principalTable: "permissions",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_switches_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "broadcasts.send",
                column: "description",
                value: "Send to your own groups and lists, pause and stop them, see their results.");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "contacts.manage",
                column: "description",
                value: "Add, edit and delete your own contacts and groups.");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "contacts.view",
                column: "description",
                value: "See your own contacts and groups.");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "name", "description", "group" },
                values: new object[,]
                {
                    { "contacts.import", "Import many contacts at once from Excel or CSV.", "Contacts" },
                    { "members.manage", "Add members to the tenant, turn their switches on or off, remove them.", "Members" },
                    { "modules.manage", "Make your own occasion modules (like Birthday) and send their wishes.", "Modules" },
                    { "platform.tenants", "Create tenants and their admins, turn them on or off.", "Platform" },
                    { "templates.manage", "Make, change and delete message templates.", "Templates" }
                });

            // Hand-written: keep every existing user and what they could do. If there are users, they all go into
            // one tenant. The Owners become its tenant admins; everyone else becomes a member whose switches
            // match what their roles allowed (import and templates were part of contacts.manage and
            // broadcasts.send before, birthday settings needed broadcasts.send).
            migrationBuilder.Sql("""
                DO $$
                DECLARE tenant uuid := gen_random_uuid();
                BEGIN
                    IF EXISTS (SELECT 1 FROM users) THEN
                        INSERT INTO tenants (id, name, is_active, created_at) VALUES (tenant, 'My company', true, now());
                        UPDATE users SET tenant_id = tenant, kind = 'member';
                        UPDATE users u SET kind = 'tenant_admin'
                        WHERE EXISTS (SELECT 1 FROM user_roles ur JOIN roles r ON r.id = ur.role_id
                                      WHERE ur.user_id = u.id AND r.is_system);
                        INSERT INTO user_switches (user_id, permission)
                        SELECT DISTINCT u.id, s.permission
                        FROM users u
                        JOIN user_roles ur ON ur.user_id = u.id
                        JOIN role_permissions rp ON rp.role_id = ur.role_id
                        JOIN (VALUES ('broadcasts.send', 'broadcasts.send'),
                                     ('broadcasts.send', 'templates.manage'),
                                     ('broadcasts.send', 'modules.manage'),
                                     ('contacts.manage', 'contacts.import')) AS s(old, permission) ON s.old = rp.permission
                        WHERE u.kind = 'member';
                    END IF;
                END $$;
                """);

            // Hand-written: one number belongs to one user. If two accounts hold the same number today, the one
            // linked last keeps it; the others are marked not linked (their WhatsApp login is not touched here,
            // the gateway reports it again when it reconnects).
            migrationBuilder.Sql("""
                UPDATE whatsapp_accounts a
                SET status = 'NotLinked', status_reason = 'number_in_use', phone_number = NULL, profile_name = NULL,
                    linked_at = NULL, status_changed_at = now()
                WHERE a.phone_number IS NOT NULL AND EXISTS (
                    SELECT 1 FROM whatsapp_accounts b
                    WHERE b.phone_number = a.phone_number AND b.id <> a.id
                      AND (b.linked_at > a.linked_at OR (b.linked_at IS NOT DISTINCT FROM a.linked_at AND b.id > a.id)
                           OR (a.linked_at IS NULL AND b.linked_at IS NOT NULL)));
                """);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_accounts_phone_number",
                table: "whatsapp_accounts",
                column: "phone_number",
                unique: true,
                filter: "phone_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_kind",
                table: "users",
                columns: new[] { "tenant_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_user_switches_permission",
                table: "user_switches",
                column: "permission");

            migrationBuilder.AddForeignKey(
                name: "fk_users_tenants_tenant_id",
                table: "users",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // Last, once the old role permissions were read above.
            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "roles.manage");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "roles.view");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "users.manage");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "users.view");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_tenants_tenant_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "user_switches");

            migrationBuilder.DropIndex(
                name: "ix_whatsapp_accounts_phone_number",
                table: "whatsapp_accounts");

            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_kind",
                table: "users");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "contacts.import");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "members.manage");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "modules.manage");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "platform.tenants");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "templates.manage");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "users");

            migrationBuilder.DropColumn(
                name: "must_change_password",
                table: "users");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "users");

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission",
                        column: x => x.permission,
                        principalTable: "permissions",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "broadcasts.send",
                column: "description",
                value: "Create, start, pause and cancel your own broadcasts, and see their progress.");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "contacts.manage",
                column: "description",
                value: "Add, edit, import and delete your own contacts, categories and groups.");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "name",
                keyValue: "contacts.view",
                column: "description",
                value: "See your own contacts, categories and groups.");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "name", "description", "group" },
                values: new object[,]
                {
                    { "roles.manage", "Create, edit and delete roles and choose their permissions.", "Roles" },
                    { "roles.view", "See roles and the permissions each one has.", "Roles" },
                    { "users.manage", "Create users, change their roles, and enable or disable them.", "Users" },
                    { "users.view", "See the list of users and their roles.", "Users" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission",
                table: "role_permissions",
                column: "permission");
        }
    }
}
