using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Tenancy.Migrations
{
    /// <summary>The company write-permission policies (company_write_insert, company_write_update, company_write_delete) on every company table of the module: a manager of company X, read-only in company Y, writes no row of Y whatever statement the code uses (critic p02 rounds 12 and 13: a branch of Y closed through ExecuteUpdate).</summary>
    public partial class CompanyWritePermission : Migration
    {
        private static readonly string[] Tables = ["companies", "branches", "user_company_access", "user_branch_access", "user_workplaces"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.ProtectCompanyWrites("tenancy", table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.UnprotectCompanyWrites("tenancy", table);
            }
        }
    }
}
