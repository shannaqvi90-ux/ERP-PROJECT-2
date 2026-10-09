using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Tenancy.Migrations
{
    /// <summary>The database requires a non-blank English legal name, the line the API already
    /// holds (tenancyLegalNameEn; p06 round 3 saw an empty English legal name saved next to an
    /// Arabic one). The Arabic legal name stays optional (needs-human #9).</summary>
    public partial class CompanyEnglishLegalNameRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_companies_legal_name",
                schema: "tenancy",
                table: "companies");

            migrationBuilder.AddCheckConstraint(
                name: "ck_companies_legal_name",
                schema: "tenancy",
                table: "companies",
                sql: "btrim(legal_name_en) <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_companies_legal_name",
                schema: "tenancy",
                table: "companies");

            migrationBuilder.AddCheckConstraint(
                name: "ck_companies_legal_name",
                schema: "tenancy",
                table: "companies",
                sql: "legal_name_en <> '' OR legal_name_ar <> ''");
        }
    }
}
