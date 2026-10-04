using System;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class AccessCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "company_count",
                schema: "tenancy",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "user_company_totals",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_count = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_company_totals", x => x.id);
                    table.UniqueConstraint("ak_user_company_totals_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_user_company_totals_count", "company_count >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_company_totals_tenant_id_user_id",
                schema: "tenancy",
                table: "user_company_totals",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.ProtectTenantTable("tenancy", "user_company_totals");
            // The workspace's company count is bookkeeping, not a change anyone made to the workspace.
            migrationBuilder.Sql("""
                DROP TRIGGER audit_capture ON tenancy.tenants;
                CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON tenancy.tenants
                    FOR EACH ROW EXECUTE FUNCTION audit.capture('-company_count');
                """);
            migrationBuilder.Sql(TenancySql.AccessCounts);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(TenancySql.AccessCountsDown);
            migrationBuilder.Sql("""
                DROP TRIGGER audit_capture ON tenancy.tenants;
                CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON tenancy.tenants
                    FOR EACH ROW EXECUTE FUNCTION audit.capture();
                """);
            migrationBuilder.UnprotectTenantTable("tenancy", "user_company_totals");
            migrationBuilder.DropTable(
                name: "user_company_totals",
                schema: "tenancy");

            migrationBuilder.DropColumn(
                name: "company_count",
                schema: "tenancy",
                table: "tenants");
        }
    }
}
