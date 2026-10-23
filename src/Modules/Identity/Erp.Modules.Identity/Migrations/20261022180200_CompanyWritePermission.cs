using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <summary>The company write-permission policies (company_write_insert, company_write_update, company_write_delete) on identity.user_company_roles: a request whose permission is held only in some companies writes no role of another company, whatever statement the code uses (critic p02 rounds 12 and 13).</summary>
    public partial class CompanyWritePermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.ProtectCompanyWrites("identity", "user_company_roles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UnprotectCompanyWrites("identity", "user_company_roles");
        }
    }
}
