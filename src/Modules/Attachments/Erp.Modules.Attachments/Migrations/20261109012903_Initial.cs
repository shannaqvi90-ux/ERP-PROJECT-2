using System;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Attachments.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "attachments");

            migrationBuilder.CreateTable(
                name: "files",
                schema: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.UniqueConstraint("ak_files_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_files_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_files_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_files_size", "size_bytes BETWEEN 1 AND 1073741824");
                    table.CheckConstraint("ck_files_storage_key", "storage_key ~ '^[0-9a-f]{32}/[0-9a-f]{32}$' AND left(storage_key, 32) = replace(tenant_id::text, '-', '')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_files_tenant_id_record_type_record_id_created_at",
                schema: "attachments",
                table: "files",
                columns: new[] { "tenant_id", "record_type", "record_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_files_tenant_id_storage_key",
                schema: "attachments",
                table: "files",
                columns: new[] { "tenant_id", "storage_key" },
                unique: true);

            migrationBuilder.GrantSchemaUsage("attachments");
            // Row-level security on tenant_id (enabled and forced) and the audit trigger on every write. The
            // store key is internal (a name the store made): the history records the file's name, kind, size,
            // checksum and record, not where the bytes sit.
            migrationBuilder.ProtectTenantTable("attachments", "files", ignore: ["storage_key"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "files",
                schema: "attachments");
        }
    }
}
