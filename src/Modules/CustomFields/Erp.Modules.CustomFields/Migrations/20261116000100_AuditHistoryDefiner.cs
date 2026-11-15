using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.CustomFields.Migrations
{
    /// <summary>
    /// Tamper evidence (p07): the audit trail has one writer, the capture trigger, which runs as the
    /// NOLOGIN role erp_auth (Erp.Kernel KernelSql.AuditTamperEvidence). The trigger that writes a custom
    /// field value's change into its record's history is the trail's other writer: it runs as erp_auth
    /// too, so the application role holds no INSERT on the trail.
    /// </summary>
    [DbContext(typeof(CustomFieldsDbContext))]
    [Migration("20261116000100_AuditHistoryDefiner")]
    public partial class AuditHistoryDefiner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
GRANT USAGE, CREATE ON SCHEMA customfields TO erp_auth;
ALTER FUNCTION customfields.audit_record_history() SECURITY DEFINER;
REVOKE ALL ON FUNCTION customfields.audit_record_history() FROM PUBLIC;
ALTER FUNCTION customfields.audit_record_history() OWNER TO erp_auth;
REVOKE CREATE ON SCHEMA customfields FROM erp_auth;
SET LOCAL ROLE erp_auth;
GRANT EXECUTE ON FUNCTION customfields.audit_record_history() TO erp_owner;
RESET ROLE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET LOCAL ROLE erp_auth;
ALTER FUNCTION customfields.audit_record_history() SECURITY INVOKER;
RESET ROLE;");
        }
    }
}
