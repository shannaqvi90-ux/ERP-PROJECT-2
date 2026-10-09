using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <summary>A user's own company roles outside their company scope stay readable to their session but are never moved into the scope or deleted there (critic p03 round 6).</summary>
    public partial class OwnCompanyRolesReadOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.KeepOwnRowsReadOnly("identity", "user_company_roles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AllowOwnRowWrites("identity", "user_company_roles");
        }
    }
}
