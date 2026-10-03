using System;
using System.Collections.Generic;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Lists.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "lists");

            migrationBuilder.CreateTable(
                name: "saved_views",
                schema: "lists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    list_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_shared = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    columns = table.Column<List<string>>(type: "text[]", nullable: false),
                    sort = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    filter = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    search = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    group_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_views", x => x.id);
                    table.UniqueConstraint("ak_saved_views_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_saved_views_name", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_saved_views_owner", "(is_shared AND owner_user_id IS NULL) OR (NOT is_shared AND owner_user_id IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_saved_views_tenant_id_list_key_is_shared_owner_user_id_name",
                schema: "lists",
                table: "saved_views",
                columns: new[] { "tenant_id", "list_key", "is_shared", "owner_user_id", "name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ux_saved_views_default",
                schema: "lists",
                table: "saved_views",
                columns: new[] { "tenant_id", "list_key", "owner_user_id" },
                unique: true,
                filter: "is_default")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.GrantSchemaUsage("lists");
            migrationBuilder.ProtectTenantTable("lists", "saved_views");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UnprotectTenantTable("lists", "saved_views");
            migrationBuilder.DropTable(
                name: "saved_views",
                schema: "lists");
        }
    }
}
