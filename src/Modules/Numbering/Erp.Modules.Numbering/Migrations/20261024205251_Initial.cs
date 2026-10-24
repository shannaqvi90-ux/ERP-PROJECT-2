using System;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Numbering.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "numbering");

            migrationBuilder.CreateTable(
                name: "sequences",
                schema: "numbering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(72)", maxLength: 72, nullable: false),
                    prefix = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    suffix = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    padding = table.Column<int>(type: "integer", nullable: false),
                    reset = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    gapless = table.Column<bool>(type: "boolean", nullable: false),
                    per_branch = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sequences", x => x.id);
                    table.UniqueConstraint("ak_sequences_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_sequences_document_type", "document_type ~ '^[a-z][a-zA-Z0-9]+\\.[a-z][a-zA-Z0-9]+$'");
                    table.CheckConstraint("ck_sequences_padding", "padding BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_sequences_reset", "reset IN ('never', 'yearly', 'monthly')");
                });

            migrationBuilder.CreateTable(
                name: "counters",
                schema: "numbering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false),
                    last_issued = table.Column<long>(type: "bigint", nullable: true),
                    last_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_counters", x => x.id);
                    table.UniqueConstraint("ak_counters_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_counters_last_issued", "last_issued IS NULL OR last_issued < next_value");
                    table.CheckConstraint("ck_counters_next_value", "next_value BETWEEN 1 AND 999999999999999999");
                    table.CheckConstraint("ck_counters_period", "period ~ '^([0-9]{4}(-[0-9]{2})?)?$'");
                    table.ForeignKey(
                        name: "fk_counters_sequences_tenant_id_sequence_id",
                        columns: x => new { x.tenant_id, x.sequence_id },
                        principalSchema: "numbering",
                        principalTable: "sequences",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "issued_numbers",
                schema: "numbering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(72)", maxLength: 72, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gapless = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issued_numbers", x => x.id);
                    table.UniqueConstraint("ak_issued_numbers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_issued_numbers_sequences_tenant_id_sequence_id",
                        columns: x => new { x.tenant_id, x.sequence_id },
                        principalSchema: "numbering",
                        principalTable: "sequences",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_counters_tenant_id_sequence_id_branch_id_period",
                schema: "numbering",
                table: "counters",
                columns: new[] { "tenant_id", "sequence_id", "branch_id", "period" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_issued_numbers_tenant_id_company_id_document_type_number",
                schema: "numbering",
                table: "issued_numbers",
                columns: new[] { "tenant_id", "company_id", "document_type", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_issued_numbers_tenant_id_sequence_id_created_at",
                schema: "numbering",
                table: "issued_numbers",
                columns: new[] { "tenant_id", "sequence_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sequences_tenant_id_company_id_document_type",
                schema: "numbering",
                table: "sequences",
                columns: new[] { "tenant_id", "company_id", "document_type" },
                unique: true);

            // Row-level security on every table (forced, for the owner too) for the tenant and the
            // company, the application role's grants and the audit trigger (who changed what, and when:
            // a sequence's settings, every number taken from a counter, every number given).
            migrationBuilder.GrantSchemaUsage("numbering");
            foreach (var table in new[] { "sequences", "counters", "issued_numbers" })
            {
                migrationBuilder.ProtectTenantTable("numbering", table);
                migrationBuilder.ProtectCompanyTable("numbering", table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "issued_numbers", "counters", "sequences" })
            {
                migrationBuilder.UnprotectCompanyTable("numbering", table);
                migrationBuilder.UnprotectTenantTable("numbering", table);
            }
            migrationBuilder.DropTable(
                name: "counters",
                schema: "numbering");

            migrationBuilder.DropTable(
                name: "issued_numbers",
                schema: "numbering");

            migrationBuilder.DropTable(
                name: "sequences",
                schema: "numbering");
        }
    }
}
