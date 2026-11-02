using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <summary>The audit trail records a user's device approval (the SHA-256 of an eight-symbol code)
    /// as "[redacted]", never the hash itself: whoever may read a user's history could otherwise find
    /// the code again by trying every code (p07 round 1; the audit rule gate
    /// Every_audited_secret_is_redacted_or_left_out). Until when the approval holds stays recorded.</summary>
    public partial class AuditRedactsDeviceApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentitySql.AuditRedactsDeviceApproval);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentitySql.AuditRedactsDeviceApprovalDown);
        }
    }
}
