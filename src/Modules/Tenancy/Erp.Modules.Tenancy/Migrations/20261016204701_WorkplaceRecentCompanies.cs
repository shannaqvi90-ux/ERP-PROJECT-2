using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class WorkplaceRecentCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "recent_company_ids",
                schema: "tenancy",
                table: "user_workplaces",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_workplaces_recent_company_ids",
                schema: "tenancy",
                table: "user_workplaces",
                sql: "cardinality(recent_company_ids) <= 6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_workplaces_recent_company_ids",
                schema: "tenancy",
                table: "user_workplaces");

            migrationBuilder.DropColumn(
                name: "recent_company_ids",
                schema: "tenancy",
                table: "user_workplaces");
        }
    }
}
