using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.CustomFields.Migrations
{
    /// <summary>
    /// Tamper evidence (p07): the audit trail has two writers, both trigger functions that run as the NOLOGIN
    /// role erp_auth (audit.capture(), Erp.Kernel KernelSql.AuditTamperEvidence, and audit.custom_field_history()
    /// here), so the application role holds no INSERT on the trail. The history trigger of a custom field value
    /// lives in the audit schema as a definer function, so customfields keeps only caller-rights functions
    /// (custom field functions run as the caller, CustomFieldGateTests); customfields.history_change() is the
    /// caller-rights part, a pure function that words the change.
    /// </summary>
    [DbContext(typeof(CustomFieldsDbContext))]
    [Migration("20261116000100_AuditHistoryDefiner")]
    public partial class AuditHistoryDefiner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE FUNCTION customfields.history_change(p_code text, p_old jsonb, p_new jsonb) RETURNS jsonb
LANGUAGE sql IMMUTABLE PARALLEL SAFE
SET search_path = pg_catalog, pg_temp
AS $$
    SELECT jsonb_build_object('custom_fields.' || p_code, jsonb_strip_nulls(jsonb_build_object('old', p_old, 'new', p_new)))
$$;

DROP TRIGGER audit_record_history ON customfields.field_values;
DROP FUNCTION customfields.audit_record_history();

GRANT USAGE ON SCHEMA customfields TO erp_auth;
GRANT USAGE, CREATE ON SCHEMA audit TO erp_auth;

CREATE FUNCTION audit.custom_field_history() RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row record := COALESCE(NEW, OLD);
    v_old jsonb;
    v_new jsonb;
    v_label text;
    v_label_ar text;
    v_code text;
    v_company uuid;
    v_branch uuid;
BEGIN
    -- Seeded values are the starting data, not changes: they have the table's own capture row only.
    IF COALESCE(current_setting('app.actor_kind', true), '') = 'seed' THEN
        RETURN NULL;
    END IF;
    IF TG_OP IN ('UPDATE', 'DELETE') THEN
        v_old := COALESCE(to_jsonb(OLD.text_value), to_jsonb(OLD.number_value), to_jsonb(OLD.date_value), to_jsonb(OLD.bool_value), to_jsonb(OLD.ref_value));
    END IF;
    IF TG_OP IN ('INSERT', 'UPDATE') THEN
        v_new := COALESCE(to_jsonb(NEW.text_value), to_jsonb(NEW.number_value), to_jsonb(NEW.date_value), to_jsonb(NEW.bool_value), to_jsonb(NEW.ref_value));
    END IF;
    IF TG_OP = 'UPDATE' AND v_old IS NOT DISTINCT FROM v_new THEN
        RETURN NULL;
    END IF;
    SELECT e.record_label, e.record_label_ar, e.record_code, e.record_company_id, e.record_branch_id
      INTO v_label, v_label_ar, v_code, v_company, v_branch
      FROM audit.entries e
     WHERE e.tenant_id = v_row.tenant_id AND e.record_type = v_row.record_type AND e.record_id = v_row.record_id
     ORDER BY e.occurred_at DESC LIMIT 1;
    INSERT INTO audit.entries (tenant_id, occurred_at, actor_id, actor_kind, table_schema, table_name, record_id, action, changes,
                               transaction_id, correlation_id, record_label, record_label_ar, record_code, record_company_id, record_branch_id)
    VALUES (v_row.tenant_id, clock_timestamp(), erp.current_actor_id(), COALESCE(NULLIF(current_setting('app.actor_kind', true), ''), 'unknown'),
            split_part(v_row.record_type, '.', 1), split_part(v_row.record_type, '.', 2), v_row.record_id, 'update',
            customfields.history_change(v_row.code, v_old, v_new),
            txid_current(), NULLIF(current_setting('app.correlation_id', true), ''), v_label, v_label_ar, v_code, v_company, v_branch);
    RETURN NULL;
END;
$$;

REVOKE ALL ON FUNCTION audit.custom_field_history() FROM PUBLIC;
ALTER FUNCTION audit.custom_field_history() OWNER TO erp_auth;
REVOKE CREATE ON SCHEMA audit FROM erp_auth;
SET LOCAL ROLE erp_auth;
GRANT EXECUTE ON FUNCTION audit.custom_field_history() TO erp_owner;
RESET ROLE;

CREATE TRIGGER audit_record_history AFTER INSERT OR UPDATE OR DELETE ON customfields.field_values FOR EACH ROW EXECUTE FUNCTION audit.custom_field_history();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("The audit trail's writers do not go back to the application role.");
        }
    }
}
