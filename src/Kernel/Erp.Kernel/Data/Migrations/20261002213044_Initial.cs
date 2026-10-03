using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Erp.Kernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.Functions);

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    table_schema = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    table_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    changes = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_actor_id_occurred_at",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "actor_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_occurred_at",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_id_table_name_record_id_occurred_at",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "table_name", "record_id", "occurred_at" });

            migrationBuilder.Sql(KernelSql.AuditSecurity);
            migrationBuilder.Sql(KernelSql.CaptureFunction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(KernelSql.Down);

            migrationBuilder.DropTable(
                name: "entries",
                schema: "audit");
        }
    }
}
