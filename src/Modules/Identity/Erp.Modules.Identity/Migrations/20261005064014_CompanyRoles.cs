using System;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class CompanyRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "company_role_count",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "user_company_roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_company_roles", x => x.id);
                    table.UniqueConstraint("ak_user_company_roles_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_user_company_roles_roles_tenant_id_role_id",
                        columns: x => new { x.tenant_id, x.role_id },
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_company_roles_users_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_company_role_count",
                schema: "identity",
                table: "users",
                sql: "company_role_count >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_user_company_roles_tenant_id_company_id",
                schema: "identity",
                table: "user_company_roles",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_company_roles_tenant_id_role_id",
                schema: "identity",
                table: "user_company_roles",
                columns: new[] { "tenant_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_company_roles_tenant_id_user_id_role_id_company_id",
                schema: "identity",
                table: "user_company_roles",
                columns: new[] { "tenant_id", "user_id", "role_id", "company_id" },
                unique: true);

            migrationBuilder.ProtectTenantTable("identity", "user_company_roles");
            migrationBuilder.ProtectCompanyTable("identity", "user_company_roles", ownRowsReadable: true);
            migrationBuilder.Sql(IdentitySql.CompanyRoles);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentitySql.CompanyRolesDown);
            migrationBuilder.UnprotectCompanyTable("identity", "user_company_roles");
            migrationBuilder.UnprotectTenantTable("identity", "user_company_roles");
            migrationBuilder.DropTable(
                name: "user_company_roles",
                schema: "identity");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_company_role_count",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "company_role_count",
                schema: "identity",
                table: "users");
        }
    }
}
