using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class ListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_companies_code_legal_name_en_legal_name_ar",
                schema: "tenancy",
                table: "companies",
                columns: new[] { "code", "legal_name_en", "legal_name_ar" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_companies_tenant_id_city_id",
                schema: "tenancy",
                table: "companies",
                columns: new[] { "tenant_id", "city", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_companies_tenant_id_legal_name_ar_id",
                schema: "tenancy",
                table: "companies",
                columns: new[] { "tenant_id", "legal_name_ar", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_code_name_en_name_ar",
                schema: "tenancy",
                table: "branches",
                columns: new[] { "code", "name_en", "name_ar" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_city_id",
                schema: "tenancy",
                table: "branches",
                columns: new[] { "tenant_id", "city", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_code_id",
                schema: "tenancy",
                table: "branches",
                columns: new[] { "tenant_id", "code", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_name_ar_id",
                schema: "tenancy",
                table: "branches",
                columns: new[] { "tenant_id", "name_ar", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_companies_code_legal_name_en_legal_name_ar",
                schema: "tenancy",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "ix_companies_tenant_id_city_id",
                schema: "tenancy",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "ix_companies_tenant_id_legal_name_ar_id",
                schema: "tenancy",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "ix_branches_code_name_en_name_ar",
                schema: "tenancy",
                table: "branches");

            migrationBuilder.DropIndex(
                name: "ix_branches_tenant_id_city_id",
                schema: "tenancy",
                table: "branches");

            migrationBuilder.DropIndex(
                name: "ix_branches_tenant_id_code_id",
                schema: "tenancy",
                table: "branches");

            migrationBuilder.DropIndex(
                name: "ix_branches_tenant_id_name_ar_id",
                schema: "tenancy",
                table: "branches");
        }
    }
}
