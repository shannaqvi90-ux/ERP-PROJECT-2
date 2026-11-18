using System;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.ImportExport.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "importexport");

            migrationBuilder.CreateTable(
                name: "import_mappings",
                schema: "importexport",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    header_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    field_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    times_used = table.Column<int>(type: "integer", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_mappings", x => x.id);
                    table.UniqueConstraint("ak_import_mappings_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_import_mappings_header", "btrim(header_key) <> ''");
                    table.CheckConstraint("ck_import_mappings_times", "times_used >= 1");
                });

            migrationBuilder.CreateTable(
                name: "import_sessions",
                schema: "importexport",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    headers = table.Column<string>(type: "jsonb", nullable: false),
                    mapping = table.Column<string>(type: "jsonb", nullable: false),
                    sources = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    sample = table.Column<string>(type: "jsonb", nullable: false),
                    on_error = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    dry_run = table.Column<bool>(type: "boolean", nullable: false),
                    rows_checked = table.Column<int>(type: "integer", nullable: false),
                    rows_valid = table.Column<int>(type: "integer", nullable: false),
                    rows_imported = table.Column<int>(type: "integer", nullable: false),
                    rows_failed = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issues = table.Column<string>(type: "jsonb", nullable: false),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_en = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    failure_ar = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_sessions", x => x.id);
                    table.UniqueConstraint("ak_import_sessions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_import_sessions_counts", "row_count >= 0 AND rows_checked >= 0 AND rows_valid >= 0 AND rows_imported >= 0 AND rows_failed >= 0");
                    table.CheckConstraint("ck_import_sessions_file_name", "btrim(file_name) <> ''");
                    table.CheckConstraint("ck_import_sessions_format", "format IN ('csv', 'xlsx')");
                    table.CheckConstraint("ck_import_sessions_on_error", "on_error IN ('reject', 'skip')");
                    table.CheckConstraint("ck_import_sessions_status", "status IN ('uploaded', 'running', 'checked', 'completed', 'failed')");
                });

            migrationBuilder.CreateTable(
                name: "tenant_exports",
                schema: "importexport",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_count = table.Column<int>(type: "integer", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    record_counts = table.Column<string>(type: "jsonb", nullable: false),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_en = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    failure_ar = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    download_count = table.Column<int>(type: "integer", nullable: false),
                    downloaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    downloaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_exports", x => x.id);
                    table.UniqueConstraint("ak_tenant_exports_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_exports_counts", "file_count >= 0 AND byte_size >= 0 AND download_count >= 0");
                    table.CheckConstraint("ck_tenant_exports_format", "format IN ('csv', 'json')");
                    table.CheckConstraint("ck_tenant_exports_status", "status IN ('queued', 'running', 'succeeded', 'failed', 'expired')");
                });

            migrationBuilder.CreateTable(
                name: "import_files",
                schema: "importexport",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    error_report = table.Column<byte[]>(type: "bytea", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_files", x => x.id);
                    table.UniqueConstraint("ak_import_files_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_import_files_size", "size_bytes BETWEEN 1 AND 104857600");
                    table.ForeignKey(
                        name: "fk_import_files_import_sessions_tenant_id_session_id",
                        columns: x => new { x.tenant_id, x.session_id },
                        principalSchema: "importexport",
                        principalTable: "import_sessions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "export_files",
                schema: "importexport",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    export_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_files", x => x.id);
                    table.UniqueConstraint("ak_export_files_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_export_files_size", "size_bytes BETWEEN 1 AND 536870912");
                    table.ForeignKey(
                        name: "fk_export_files_tenant_exports_tenant_id_export_id",
                        columns: x => new { x.tenant_id, x.export_id },
                        principalSchema: "importexport",
                        principalTable: "tenant_exports",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_export_files_tenant_id_export_id",
                schema: "importexport",
                table: "export_files",
                columns: new[] { "tenant_id", "export_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_files_tenant_id_session_id",
                schema: "importexport",
                table: "import_files",
                columns: new[] { "tenant_id", "session_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_mappings_tenant_id_record_type_header_key",
                schema: "importexport",
                table: "import_mappings",
                columns: new[] { "tenant_id", "record_type", "header_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_sessions_tenant_id_record_type_created_at",
                schema: "importexport",
                table: "import_sessions",
                columns: new[] { "tenant_id", "record_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_exports_tenant_id_created_at",
                schema: "importexport",
                table: "tenant_exports",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.GrantSchemaUsage("importexport");
            // Row-level security on tenant_id (enabled and forced) and the audit trigger on every write. The bytes of files are not
            // copied into the audit trail (the history records each file's size); the first rows of an upload, its header row and
            // the first problems stay out of it too (they are the workspace's own data, kept on the record).
            migrationBuilder.ProtectTenantTable("importexport", "import_sessions", ignore: ["headers", "sample", "issues"]);
            migrationBuilder.ProtectTenantTable("importexport", "import_files", ignore: ["content", "error_report"]);
            migrationBuilder.ProtectTenantTable("importexport", "import_mappings");
            migrationBuilder.ProtectTenantTable("importexport", "tenant_exports");
            migrationBuilder.ProtectTenantTable("importexport", "export_files", ignore: ["content"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_files",
                schema: "importexport");

            migrationBuilder.DropTable(
                name: "import_files",
                schema: "importexport");

            migrationBuilder.DropTable(
                name: "import_mappings",
                schema: "importexport");

            migrationBuilder.DropTable(
                name: "tenant_exports",
                schema: "importexport");

            migrationBuilder.DropTable(
                name: "import_sessions",
                schema: "importexport");
        }
    }
}
