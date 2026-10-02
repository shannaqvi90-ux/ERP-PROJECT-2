using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Kernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantBoundToTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.TransactionBoundTenant);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.TransactionBoundTenantDown);
        }
    }
}
