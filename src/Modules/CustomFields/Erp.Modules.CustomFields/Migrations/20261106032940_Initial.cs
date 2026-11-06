using System;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.CustomFields.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customfields");

            migrationBuilder.CreateTable(
                name: "definitions",
                schema: "customfields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    label_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    field_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    default_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decimals = table.Column<int>(type: "integer", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    lookup_target = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    searchable = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_definitions", x => x.id);
                    table.UniqueConstraint("ak_definitions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_definitions_decimals", "decimals BETWEEN 0 AND 8");
                    table.CheckConstraint("ck_definitions_key", "key ~ '^cf[A-Za-z0-9]{1,38}$'");
                    table.CheckConstraint("ck_definitions_labels", "btrim(label_en) <> '' AND btrim(label_ar) <> ''");
                    table.CheckConstraint("ck_definitions_lookup", "(field_type = 'lookup') = (lookup_target IS NOT NULL)");
                    table.CheckConstraint("ck_definitions_options", "(field_type = 'select' AND jsonb_array_length(options) BETWEEN 1 AND 50) OR (field_type <> 'select' AND jsonb_array_length(options) = 0)");
                    table.CheckConstraint("ck_definitions_type", "field_type IN ('text', 'number', 'date', 'boolean', 'select', 'lookup')");
                });

            migrationBuilder.CreateTable(
                name: "field_values",
                schema: "customfields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    number_value = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    date_value = table.Column<DateOnly>(type: "date", nullable: true),
                    bool_value = table.Column<bool>(type: "boolean", nullable: true),
                    ref_value = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_values", x => x.id);
                    table.UniqueConstraint("ak_field_values_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_field_values_one_column", "num_nonnulls(text_value, number_value, date_value, bool_value, ref_value) = 1");
                    table.ForeignKey(
                        name: "fk_field_values_definitions_tenant_id_field_id",
                        columns: x => new { x.tenant_id, x.field_id },
                        principalSchema: "customfields",
                        principalTable: "definitions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_definitions_tenant_id_record_type_key",
                schema: "customfields",
                table: "definitions",
                columns: new[] { "tenant_id", "record_type", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_definitions_tenant_id_record_type_sort_order",
                schema: "customfields",
                table: "definitions",
                columns: new[] { "tenant_id", "record_type", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_field_values_tenant_id_field_id_bool_value_record_id",
                schema: "customfields",
                table: "field_values",
                columns: new[] { "tenant_id", "field_id", "bool_value", "record_id" },
                filter: "bool_value IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_field_values_tenant_id_field_id_date_value_record_id",
                schema: "customfields",
                table: "field_values",
                columns: new[] { "tenant_id", "field_id", "date_value", "record_id" },
                filter: "date_value IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_field_values_tenant_id_field_id_number_value_record_id",
                schema: "customfields",
                table: "field_values",
                columns: new[] { "tenant_id", "field_id", "number_value", "record_id" },
                filter: "number_value IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_field_values_tenant_id_field_id_ref_value_record_id",
                schema: "customfields",
                table: "field_values",
                columns: new[] { "tenant_id", "field_id", "ref_value", "record_id" },
                filter: "ref_value IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_field_values_tenant_id_record_id_field_id",
                schema: "customfields",
                table: "field_values",
                columns: new[] { "tenant_id", "record_id", "field_id" },
                unique: true);

            migrationBuilder.GrantSchemaUsage("customfields");
            // Row-level security on tenant_id (enabled and forced) and the audit trigger on every write.
            migrationBuilder.ProtectTenantTable("customfields", "definitions");
            migrationBuilder.ProtectTenantTable("customfields", "field_values");

            // Value indexes by kind (the number, date, yes/no and lookup ones are made above): a field's
            // text values by lower-case text for equality and prefix reads, and by trigram for "contains".
            migrationBuilder.Sql("CREATE INDEX ix_field_values_text ON customfields.field_values (tenant_id, field_id, lower(text_value), record_id) WHERE text_value IS NOT NULL;");
            migrationBuilder.Sql("CREATE INDEX ix_field_values_text_trgm ON customfields.field_values USING gin (text_value gin_trgm_ops) WHERE text_value IS NOT NULL;");

            // The questions a list asks of the values (Erp.Kernel.CustomFields.CustomFieldSql). Each answers
            // the ids of the records whose value matches and runs as the caller, so row-level security keeps
            // it inside the caller's workspace; a list says "id IN (match…)" and the planner joins the value
            // indexes to the records. One function per kind of value; the operator is a plain argument whose
            // branches the planner drops when it is a constant.
            migrationBuilder.Sql(@"
CREATE FUNCTION customfields.match_text(field uuid, op text, val text) RETURNS TABLE (record_id uuid)
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT v.record_id FROM customfields.field_values v
     WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'eq' AND lower(v.text_value) = lower(val)
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v
     WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'contains'
       AND v.text_value ILIKE '%' || replace(replace(replace(val, '\', '\\'), '%', '\%'), '_', '\_') || '%' ESCAPE '\'
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v
     WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'startswith'
       AND v.text_value ILIKE replace(replace(replace(val, '\', '\\'), '%', '\%'), '_', '\_') || '%' ESCAPE '\'
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v
     WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'endswith'
       AND v.text_value ILIKE '%' || replace(replace(replace(val, '\', '\\'), '%', '\%'), '_', '\_') ESCAPE '\'
$$;");
            migrationBuilder.Sql(@"
CREATE FUNCTION customfields.match_number(field uuid, op text, val numeric) RETURNS TABLE (record_id uuid)
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'eq' AND v.number_value = val
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'lt' AND v.number_value < val
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'le' AND v.number_value <= val
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'gt' AND v.number_value > val
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'ge' AND v.number_value >= val
$$;");
            migrationBuilder.Sql(@"
CREATE FUNCTION customfields.match_date(field uuid, op text, val date) RETURNS TABLE (record_id uuid)
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'eq' AND v.date_value = val
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'lt' AND v.date_value < val
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'le' AND v.date_value <= val
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'gt' AND v.date_value > val
    UNION ALL
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND op = 'ge' AND v.date_value >= val
$$;");
            migrationBuilder.Sql(@"
CREATE FUNCTION customfields.match_bool(field uuid, val boolean) RETURNS TABLE (record_id uuid)
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND v.bool_value = val
$$;");
            migrationBuilder.Sql(@"
CREATE FUNCTION customfields.match_ref(field uuid, val uuid) RETURNS TABLE (record_id uuid)
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field AND v.ref_value = val
$$;");
            migrationBuilder.Sql(@"
CREATE FUNCTION customfields.match_any(field uuid) RETURNS TABLE (record_id uuid)
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT v.record_id FROM customfields.field_values v WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = field
$$;");
            migrationBuilder.Sql(@"
CREATE FUNCTION customfields.search_text(fields uuid[], pattern text) RETURNS TABLE (record_id uuid)
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT v.record_id FROM customfields.field_values v
     WHERE v.tenant_id = erp.current_tenant_id() AND v.field_id = ANY (fields) AND v.text_value ILIKE pattern ESCAPE '\'
$$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "field_values",
                schema: "customfields");

            migrationBuilder.DropTable(
                name: "definitions",
                schema: "customfields");
        }
    }
}
