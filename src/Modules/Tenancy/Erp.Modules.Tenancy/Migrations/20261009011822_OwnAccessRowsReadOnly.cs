using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Tenancy.Migrations
{
    /// <summary>A user's own company and branch access outside their company scope stays readable to their session but is never moved into the scope or deleted there (critic p03 round 6). The workplace table keeps its own-row writes (tests/Gates/own-row-writes.txt).</summary>
    public partial class OwnAccessRowsReadOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.KeepOwnRowsReadOnly("tenancy", "user_company_access");
            migrationBuilder.KeepOwnRowsReadOnly("tenancy", "user_branch_access");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AllowOwnRowWrites("tenancy", "user_company_access");
            migrationBuilder.AllowOwnRowWrites("tenancy", "user_branch_access");
        }
    }
}
