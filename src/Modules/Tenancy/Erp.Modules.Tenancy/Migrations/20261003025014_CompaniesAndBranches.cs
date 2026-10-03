using Erp.Kernel.Data;
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class CompaniesAndBranches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_language",
                schema: "tenancy",
                table: "tenants",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                schema: "tenancy",
                table: "tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Asia/Dubai");

            migrationBuilder.AddColumn<string>(
                name: "week_start",
                schema: "tenancy",
                table: "tenants",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "monday");

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    legal_name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    legal_name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_licence_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    trade_licence_authority = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tax_registration_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    base_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fiscal_year_start_month = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year_start_day = table.Column<int>(type: "integer", nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    emirate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    po_box = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    address_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    website = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    logo = table.Column<byte[]>(type: "bytea", nullable: true),
                    logo_content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    logo_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                    table.UniqueConstraint("ak_companies_tenant_id_company_id", x => new { x.tenant_id, x.company_id });
                    table.UniqueConstraint("ak_companies_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_companies_base_currency", "base_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_companies_code", "code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'");
                    table.CheckConstraint("ck_companies_company_is_self", "company_id = id");
                    table.CheckConstraint("ck_companies_country", "country ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("ck_companies_fiscal_year_start", "fiscal_year_start_month BETWEEN 1 AND 12 AND fiscal_year_start_day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_companies_logo", "(logo IS NULL) = (logo_content_type IS NULL) AND (logo IS NULL) = (logo_hash IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    emirate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    po_box = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    address_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                    table.UniqueConstraint("ak_branches_tenant_id_company_id_id", x => new { x.tenant_id, x.company_id, x.id });
                    table.UniqueConstraint("ak_branches_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_branches_code", "code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'");
                    table.CheckConstraint("ck_branches_country", "country ~ '^[A-Z]{2}$'");
                    table.ForeignKey(
                        name: "fk_branches_companies_tenant_id_company_id",
                        columns: x => new { x.tenant_id, x.company_id },
                        principalSchema: "tenancy",
                        principalTable: "companies",
                        principalColumns: new[] { "tenant_id", "company_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_company_access",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    all_branches = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_company_access", x => x.id);
                    table.UniqueConstraint("ak_user_company_access_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.UniqueConstraint("ak_user_company_access_tenant_id_user_id_company_id", x => new { x.tenant_id, x.user_id, x.company_id });
                    table.ForeignKey(
                        name: "fk_user_company_access_companies_tenant_id_company_id",
                        columns: x => new { x.tenant_id, x.company_id },
                        principalSchema: "tenancy",
                        principalTable: "companies",
                        principalColumns: new[] { "tenant_id", "company_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_branch_access",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_branch_access", x => x.id);
                    table.UniqueConstraint("ak_user_branch_access_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_user_branch_access_branches_tenant_id_company_id_branch_id",
                        columns: x => new { x.tenant_id, x.company_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_branch_access_user_company_access_tenant_id_user_id_co",
                        columns: x => new { x.tenant_id, x.user_id, x.company_id },
                        principalSchema: "tenancy",
                        principalTable: "user_company_access",
                        principalColumns: new[] { "tenant_id", "user_id", "company_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_workplaces",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_workplaces", x => x.id);
                    table.UniqueConstraint("ak_user_workplaces_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_user_workplaces_branches_tenant_id_company_id_branch_id",
                        columns: x => new { x.tenant_id, x.company_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_workplaces_user_company_access_tenant_id_user_id_compa",
                        columns: x => new { x.tenant_id, x.user_id, x.company_id },
                        principalSchema: "tenancy",
                        principalTable: "user_company_access",
                        principalColumns: new[] { "tenant_id", "user_id", "company_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_default_language",
                schema: "tenancy",
                table: "tenants",
                sql: "default_language IN ('en', 'ar')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_week_start",
                schema: "tenancy",
                table: "tenants",
                sql: "week_start IN ('monday', 'sunday', 'saturday')");

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_company_id_code",
                schema: "tenancy",
                table: "branches",
                columns: new[] { "tenant_id", "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_name_en",
                schema: "tenancy",
                table: "branches",
                columns: new[] { "tenant_id", "name_en" });

            migrationBuilder.CreateIndex(
                name: "ix_companies_tenant_id_code",
                schema: "tenancy",
                table: "companies",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_companies_tenant_id_legal_name_en",
                schema: "tenancy",
                table: "companies",
                columns: new[] { "tenant_id", "legal_name_en" });

            migrationBuilder.CreateIndex(
                name: "ix_user_branch_access_tenant_id_company_id_branch_id",
                schema: "tenancy",
                table: "user_branch_access",
                columns: new[] { "tenant_id", "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_branch_access_tenant_id_user_id_branch_id",
                schema: "tenancy",
                table: "user_branch_access",
                columns: new[] { "tenant_id", "user_id", "branch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_branch_access_tenant_id_user_id_company_id",
                schema: "tenancy",
                table: "user_branch_access",
                columns: new[] { "tenant_id", "user_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_company_access_tenant_id_company_id",
                schema: "tenancy",
                table: "user_company_access",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_workplaces_tenant_id_company_id",
                schema: "tenancy",
                table: "user_workplaces",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_workplaces_tenant_id_company_id_branch_id",
                schema: "tenancy",
                table: "user_workplaces",
                columns: new[] { "tenant_id", "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_workplaces_tenant_id_user_id",
                schema: "tenancy",
                table: "user_workplaces",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_workplaces_tenant_id_user_id_company_id",
                schema: "tenancy",
                table: "user_workplaces",
                columns: new[] { "tenant_id", "user_id", "company_id" });

            // Row-level security: the tenant policy on every table, and the company scope on top
            // (a user's own access and workplace rows stay readable before their scope exists).
            migrationBuilder.ProtectTenantTable("tenancy", "companies", ignore: ["logo"]);
            migrationBuilder.ProtectCompanyTable("tenancy", "companies");
            migrationBuilder.ProtectTenantTable("tenancy", "branches");
            migrationBuilder.ProtectCompanyTable("tenancy", "branches");
            migrationBuilder.ProtectTenantTable("tenancy", "user_company_access");
            migrationBuilder.ProtectCompanyTable("tenancy", "user_company_access", ownRowsReadable: true);
            migrationBuilder.ProtectTenantTable("tenancy", "user_branch_access");
            migrationBuilder.ProtectCompanyTable("tenancy", "user_branch_access", ownRowsReadable: true);
            migrationBuilder.ProtectTenantTable("tenancy", "user_workplaces");
            migrationBuilder.ProtectCompanyTable("tenancy", "user_workplaces", ownRowsReadable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "user_workplaces", "user_branch_access", "user_company_access", "branches", "companies" })
            {
                migrationBuilder.UnprotectCompanyTable("tenancy", table);
                migrationBuilder.UnprotectTenantTable("tenancy", table);
            }

            migrationBuilder.DropTable(
                name: "user_branch_access",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "user_workplaces",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "user_company_access",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "tenancy");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_default_language",
                schema: "tenancy",
                table: "tenants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_week_start",
                schema: "tenancy",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "default_language",
                schema: "tenancy",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "time_zone",
                schema: "tenancy",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "week_start",
                schema: "tenancy",
                table: "tenants");
        }
    }
}
