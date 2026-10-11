using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Tenancy.Migrations
{
    /// <summary>Owner decisions of 2026-10-10 (docs/compliance/): a Tax Registration Number is exactly
    /// 15 digits, stored without spaces or dashes (numbers stored before that do not meet it are
    /// kept, as they were, in legacy_tax_registration_number); and each company's own setting that
    /// makes its Arabic legal name required. The database holds both lines the API holds.</summary>
    public partial class CompanyTaxNumberFifteenDigitsAndArabicNameSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "arabic_legal_name_required",
                schema: "tenancy",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "legacy_tax_registration_number",
                schema: "tenancy",
                table: "companies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Stored numbers first: those that are 15 digits once their spaces and dashes are gone
            // are kept in that form; any other is moved, as it was, to legacy_tax_registration_number.
            migrationBuilder.Sql(TenancySql.TaxNumbersToFifteenDigits);

            migrationBuilder.AlterColumn<string>(
                name: "tax_registration_number",
                schema: "tenancy",
                table: "companies",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_companies_legal_name_ar_required",
                schema: "tenancy",
                table: "companies",
                sql: "NOT arabic_legal_name_required OR btrim(legal_name_ar) <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "ck_companies_tax_registration_number",
                schema: "tenancy",
                table: "companies",
                sql: "tax_registration_number ~ '^[0-9]{15}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_companies_legal_name_ar_required",
                schema: "tenancy",
                table: "companies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_companies_tax_registration_number",
                schema: "tenancy",
                table: "companies");

            migrationBuilder.AlterColumn<string>(
                name: "tax_registration_number",
                schema: "tenancy",
                table: "companies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(15)",
                oldMaxLength: 15,
                oldNullable: true);

            migrationBuilder.Sql(TenancySql.TaxNumbersToFifteenDigitsDown);

            migrationBuilder.DropColumn(
                name: "arabic_legal_name_required",
                schema: "tenancy",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "legacy_tax_registration_number",
                schema: "tenancy",
                table: "companies");
        }
    }
}
