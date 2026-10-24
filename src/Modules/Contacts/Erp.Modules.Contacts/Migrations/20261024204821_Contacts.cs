using Erp.Kernel.Data;
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class Contacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contacts");

            migrationBuilder.CreateTable(
                name: "contacts",
                schema: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    job_title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    mobile = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    website = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    emirate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    po_box = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    address_ar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    tags = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    trade_licence_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tax_registration_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    default_currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false),
                    credit_limit_amount = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    credit_limit_currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: true),
                    credit_limit_exchange_rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: true),
                    credit_limit_base_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    credit_limit_base_currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: true),
                    credit_limit_stated = table.Column<bool>(type: "boolean", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    name_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_ar_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email_key = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone_key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    mobile_key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                    table.UniqueConstraint("ak_contacts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_contacts_country", "country ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("ck_contacts_credit_limit", "(credit_limit_amount IS NULL AND credit_limit_currency IS NULL AND credit_limit_exchange_rate IS NULL AND credit_limit_base_amount IS NULL AND credit_limit_base_currency IS NULL AND credit_limit_stated IS NULL) OR (credit_limit_amount >= 0 AND credit_limit_currency ~ '^[A-Z]{3}$' AND credit_limit_exchange_rate > 0 AND credit_limit_base_amount >= 0 AND credit_limit_base_currency ~ '^[A-Z]{3}$' AND credit_limit_stated IS NOT NULL AND (credit_limit_currency <> credit_limit_base_currency OR credit_limit_exchange_rate = 1))");
                    table.CheckConstraint("ck_contacts_default_currency", "default_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_contacts_emirate", "emirate IS NULL OR (country = 'AE' AND emirate IN ('abuDhabi', 'dubai', 'sharjah', 'ajman', 'ummAlQuwain', 'rasAlKhaimah', 'fujairah'))");
                    table.CheckConstraint("ck_contacts_kind", "kind IN ('organisation', 'person')");
                    table.CheckConstraint("ck_contacts_name", "btrim(name_en) <> '' OR btrim(name_ar) <> ''");
                    table.CheckConstraint("ck_contacts_organisation", "organisation_id IS NULL OR (kind = 'person' AND organisation_id <> id)");
                    table.CheckConstraint("ck_contacts_tax_registration_number", "tax_registration_number ~ '^[0-9]{15}$'");
                    table.ForeignKey(
                        name: "fk_contacts_contacts_tenant_id_organisation_id",
                        columns: x => new { x.tenant_id, x.organisation_id },
                        principalSchema: "contacts",
                        principalTable: "contacts",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_name_en_name_ar_email_reference_phone_mobile_tax_r",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "name_en", "name_ar", "email", "reference", "phone", "mobile", "tax_registration_number" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_city_id",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "city", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_created_at_id",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_email_id",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "email", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_email_key",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "email_key" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_mobile_key",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "mobile_key" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_name_ar_key",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "name_ar_key" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_name_en_id",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "name_en", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_name_key",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "name_key" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_organisation_id",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_phone_key",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "phone_key" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_reference",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "reference" },
                unique: true,
                filter: "reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_reference_id",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "reference", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_tax_registration_number",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "tax_registration_number" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_updated_at_id",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "updated_at", "id" });

            migrationBuilder.GrantSchemaUsage("contacts");
            // Row-level security on tenant_id (enabled and forced) and the audit trigger on every write.
            // The derived duplicate keys are left out of the change set (they follow the name, e-mail
            // and numbers, which are recorded).
            migrationBuilder.ProtectTenantTable("contacts", "contacts", ignore: ["name_key", "name_ar_key", "email_key", "phone_key", "mobile_key"]);
            // Contacts are archived, never deleted (documents keep naming them): the application
            // role may not delete one at all.
            migrationBuilder.Sql($"REVOKE DELETE ON contacts.contacts FROM {DatabaseRoles.App};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contacts",
                schema: "contacts");
        }
    }
}
