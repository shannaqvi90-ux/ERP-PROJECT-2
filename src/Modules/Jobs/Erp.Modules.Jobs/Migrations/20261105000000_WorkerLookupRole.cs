using Erp.Modules.Jobs;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Jobs.Migrations
{
    /// <summary>The worker's cross-workspace lookup moves to the login role erp_worker, so its process holds no owner credentials.</summary>
    [DbContext(typeof(JobsDbContext))]
    [Migration("20261105000000_WorkerLookupRole")]
    public partial class WorkerLookupRole : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(JobsSql.WorkerRoleUp);

        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(JobsSql.WorkerRoleDown);
    }
}
