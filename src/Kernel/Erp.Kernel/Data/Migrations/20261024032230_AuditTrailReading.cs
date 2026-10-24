using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Kernel.Data.Migrations
{
    /// <summary>
    /// The audit trail screens (p07): each row names its record (label in English and Arabic, and its
    /// e-mail or code), keeps the record's company (read by the RESTRICTIVE <c>audit_company_scope</c>
    /// policy, so a reader sees the history of the companies in their scope only) and branch, a unique
    /// id for list paging, the record type and the changed field names (both generated), with the
    /// indexes the audit log list sorts, searches and filters by. The capture trigger fills the new
    /// columns and records custom fields field by field (<see cref="KernelSql.CaptureFunctionV2"/>).
    /// </summary>
    public partial class AuditTrailReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.FieldNamesFunction);

            migrationBuilder.DropIndex(
                name: "ix_entries_tenant_id_occurred_at",
                schema: "audit",
                table: "entries");

            migrationBuilder.AddColumn<Guid>(
                name: "record_branch_id",
                schema: "audit",
                table: "entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "record_code",
                schema: "audit",
                table: "entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "record_company_id",
                schema: "audit",
                table: "entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "record_label",
                schema: "audit",
                table: "entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "record_label_ar",
                schema: "audit",
                table: "entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "uid",
                schema: "audit",
                table: "entries",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<string[]>(
                name: "fields",
                schema: "audit",
                table: "entries",
                type: "text[]",
                nullable: true,
                computedColumnSql: "audit.field_names(changes)",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "record_type",
                schema: "audit",
                table: "entries",
                type: "character varying(127)",
                maxLength: 127,
                nullable: false,
                computedColumnSql: "((table_schema)::text || '.' || (table_name)::text)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_entries_fields",
                schema: "audit",
                table: "entries",
                column: "fields")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_entries_record_code",
                schema: "audit",
                table: "entries",
                column: "record_code")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_record_label",
                schema: "audit",
                table: "entries",
                column: "record_label")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_record_label_ar",
                schema: "audit",
                table: "entries",
                column: "record_label_ar")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_occurred_at_uid",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "occurred_at", "uid" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_record_type_record_id_occurred_at",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "record_type", "record_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_uid",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "uid" },
                unique: true);

            migrationBuilder.Sql(KernelSql.AuditCompanyScope);
            migrationBuilder.Sql(KernelSql.CaptureFunctionV2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.CaptureFunction);
            migrationBuilder.Sql(KernelSql.AuditCompanyScopeDown);

            migrationBuilder.DropIndex(
                name: "ix_entries_fields",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "ix_entries_record_code",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "ix_entries_record_label",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "ix_entries_record_label_ar",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "ix_entries_tenant_id_occurred_at_uid",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "ix_entries_tenant_id_record_type_record_id_occurred_at",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "ix_entries_tenant_id_uid",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "fields",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "record_type",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "record_branch_id",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "record_code",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "record_company_id",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "record_label",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "record_label_ar",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "uid",
                schema: "audit",
                table: "entries");

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_occurred_at",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.Sql(KernelSql.FieldNamesFunctionDown);
        }
    }
}
