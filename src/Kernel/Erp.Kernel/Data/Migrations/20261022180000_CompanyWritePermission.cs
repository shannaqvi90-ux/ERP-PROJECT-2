using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Kernel.Data.Migrations
{
    /// <summary>erp.company_writable(company_id), read by the company_write_* policies: a request whose permission is held only in some companies writes rows of those companies alone, whatever statement it uses (critic p02 rounds 12 and 13).</summary>
    public partial class CompanyWritePermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.CompanyWrite);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.CompanyWriteDown);
        }
    }
}
