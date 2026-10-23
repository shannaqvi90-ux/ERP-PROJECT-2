using System;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Currency.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "currency");

            migrationBuilder.CreateTable(
                name: "currencies",
                schema: "currency",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    max_rate_age_days = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.id);
                    table.UniqueConstraint("ak_currencies_tenant_id_code", x => new { x.tenant_id, x.code });
                    table.UniqueConstraint("ak_currencies_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_currencies_code", "code ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_currencies_max_rate_age_days", "max_rate_age_days BETWEEN 0 AND 366");
                    table.CheckConstraint("ck_currencies_names", "btrim(name_en) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                schema: "currency",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    quote_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                    table.UniqueConstraint("ak_exchange_rates_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_exchange_rates_currency", "currency ~ '^[A-Z]{3}$' AND quote_currency ~ '^[A-Z]{3}$' AND currency <> quote_currency");
                    table.CheckConstraint("ck_exchange_rates_date", "rate_date BETWEEN DATE '1990-01-01' AND DATE '2100-12-31'");
                    table.CheckConstraint("ck_exchange_rates_rate", "rate > 0");
                    table.CheckConstraint("ck_exchange_rates_source", "source IN ('manual', 'import')");
                    table.ForeignKey(
                        name: "fk_exchange_rates_currencies_tenant_id_currency",
                        columns: x => new { x.tenant_id, x.currency },
                        principalSchema: "currency",
                        principalTable: "currencies",
                        principalColumns: new[] { "tenant_id", "code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exchange_rates_currencies_tenant_id_quote_currency",
                        columns: x => new { x.tenant_id, x.quote_currency },
                        principalSchema: "currency",
                        principalTable: "currencies",
                        principalColumns: new[] { "tenant_id", "code" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_currency_reference",
                schema: "currency",
                table: "exchange_rates",
                columns: new[] { "currency", "reference" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops", "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_tenant_id_currency_id",
                schema: "currency",
                table: "exchange_rates",
                columns: new[] { "tenant_id", "currency", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_tenant_id_currency_quote_currency_rate_date",
                schema: "currency",
                table: "exchange_rates",
                columns: new[] { "tenant_id", "currency", "quote_currency", "rate_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_tenant_id_quote_currency",
                schema: "currency",
                table: "exchange_rates",
                columns: new[] { "tenant_id", "quote_currency" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_tenant_id_rate_date_id",
                schema: "currency",
                table: "exchange_rates",
                columns: new[] { "tenant_id", "rate_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_tenant_id_rate_id",
                schema: "currency",
                table: "exchange_rates",
                columns: new[] { "tenant_id", "rate", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_tenant_id_updated_at_id",
                schema: "currency",
                table: "exchange_rates",
                columns: new[] { "tenant_id", "updated_at", "id" });

            // Row-level security on every table (forced, for the owner too), the application role's
            // grants and the audit trigger (who changed what, and when).
            migrationBuilder.GrantSchemaUsage("currency");
            migrationBuilder.ProtectTenantTable("currency", "currencies");
            migrationBuilder.ProtectTenantTable("currency", "exchange_rates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UnprotectTenantTable("currency", "exchange_rates");
            migrationBuilder.UnprotectTenantTable("currency", "currencies");
            migrationBuilder.DropTable(
                name: "exchange_rates",
                schema: "currency");

            migrationBuilder.DropTable(
                name: "currencies",
                schema: "currency");
        }
    }
}
