using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Kernel.Data.Migrations
{
    /// <summary>Audit log speed (p07 round 4): the field filter gets a leakproof text form with a trigram index (an array
    /// overlap cannot use its GIN index under row-level security), and the time index covers what a deep page skips by.</summary>
    public partial class AuditLogSpeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.FieldListFunction);

            migrationBuilder.DropIndex(
                name: "ix_entries_tenant_id_occurred_at_uid",
                schema: "audit",
                table: "entries");

            migrationBuilder.AddColumn<string>(
                name: "field_list",
                schema: "audit",
                table: "entries",
                type: "text",
                nullable: true,
                computedColumnSql: "audit.field_list(changes)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_entries_field_list",
                schema: "audit",
                table: "entries",
                column: "field_list")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_occurred_at_uid",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "occurred_at", "uid" })
                .Annotation("Npgsql:IndexInclude", new[] { "record_type", "record_company_id", "record_branch_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_entries_field_list",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropIndex(
                name: "ix_entries_tenant_id_occurred_at_uid",
                schema: "audit",
                table: "entries");

            migrationBuilder.DropColumn(
                name: "field_list",
                schema: "audit",
                table: "entries");

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_occurred_at_uid",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "occurred_at", "uid" });

            migrationBuilder.Sql(KernelSql.FieldListFunctionDown);
        }
    }
}
