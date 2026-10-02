using System;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                    table.UniqueConstraint("ak_tenants_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenants_code", "code ~ '^[a-z0-9][a-z0-9-]{1,39}$'");
                    table.CheckConstraint("ck_tenants_status", "status IN ('active', 'suspended')");
                    table.CheckConstraint("ck_tenants_tenant_is_self", "tenant_id = id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_code",
                schema: "tenancy",
                table: "tenants",
                column: "code",
                unique: true);

            migrationBuilder.GrantSchemaUsage("tenancy");
            migrationBuilder.ProtectTenantTable("tenancy", "tenants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UnprotectTenantTable("tenancy", "tenants");
            migrationBuilder.DropTable(
                name: "tenants",
                schema: "tenancy");
        }
    }
}
