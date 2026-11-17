using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "attachment_id",
                table: "broadcasts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target",
                table: "broadcasts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "target_date",
                table: "broadcasts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_id",
                table: "broadcasts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "message_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_templates_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stored_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_stored_files_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_broadcasts_attachment_id",
                table: "broadcasts",
                column: "attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_broadcasts_user_id_target_target_id",
                table: "broadcasts",
                columns: new[] { "user_id", "target", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_message_templates_user_id",
                table: "message_templates",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_user_id",
                table: "stored_files",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_broadcasts_stored_files_attachment_id",
                table: "broadcasts",
                column: "attachment_id",
                principalTable: "stored_files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_broadcasts_stored_files_attachment_id",
                table: "broadcasts");

            migrationBuilder.DropTable(
                name: "message_templates");

            migrationBuilder.DropTable(
                name: "stored_files");

            migrationBuilder.DropIndex(
                name: "ix_broadcasts_attachment_id",
                table: "broadcasts");

            migrationBuilder.DropIndex(
                name: "ix_broadcasts_user_id_target_target_id",
                table: "broadcasts");

            migrationBuilder.DropColumn(
                name: "attachment_id",
                table: "broadcasts");

            migrationBuilder.DropColumn(
                name: "target",
                table: "broadcasts");

            migrationBuilder.DropColumn(
                name: "target_date",
                table: "broadcasts");

            migrationBuilder.DropColumn(
                name: "target_id",
                table: "broadcasts");
        }
    }
}
