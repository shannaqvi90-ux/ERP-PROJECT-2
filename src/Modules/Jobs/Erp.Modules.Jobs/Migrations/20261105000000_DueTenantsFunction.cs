using Erp.Modules.Jobs;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Jobs.Migrations
{
    /// <summary>The worker finds due workspaces through jobs.due_tenants() on the application role, so its process holds no owner credentials.</summary>
    [DbContext(typeof(JobsDbContext))]
    [Migration("20261105000000_DueTenantsFunction")]
    public partial class DueTenantsFunction : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(JobsSql.DueTenantsFunctionUp);

        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(JobsSql.DueTenantsFunctionDown);
    }
}
