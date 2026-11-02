using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Kernel.Data.Migrations
{
    /// <summary>Tamper evidence (p07): audit.capture() becomes the only writer of the audit trail (see <see cref="KernelSql.AuditTamperEvidence"/>).</summary>
    public partial class AuditTamperEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.AuditTamperEvidence);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.AuditTamperEvidenceDown);
        }
    }
}
