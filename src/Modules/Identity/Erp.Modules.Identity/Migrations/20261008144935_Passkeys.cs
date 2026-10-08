using System;
using System.Collections.Generic;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class Passkeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "passkeys",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<byte[]>(type: "bytea", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    algorithm = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sign_count = table.Column<long>(type: "bigint", nullable: false),
                    backup_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    backed_up = table.Column<bool>(type: "boolean", nullable: false),
                    transports = table.Column<List<string>>(type: "text[]", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_challenge_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passkeys", x => x.id);
                    table.UniqueConstraint("ak_passkeys_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_passkeys_algorithm", "algorithm IN (-7, -257)");
                    table.CheckConstraint("ck_passkeys_credential_id", "octet_length(credential_id) BETWEEN 16 AND 1023");
                    table.CheckConstraint("ck_passkeys_sign_count", "sign_count >= 0");
                    table.ForeignKey(
                        name: "fk_passkeys_users_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_passkeys_tenant_id_credential_id",
                schema: "identity",
                table: "passkeys",
                columns: new[] { "tenant_id", "credential_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_passkeys_tenant_id_user_id_created_at",
                schema: "identity",
                table: "passkeys",
                columns: new[] { "tenant_id", "user_id", "created_at" });

            // Row-level security and the audit trail; what changes at each sign-in (the last use,
            // the last challenge answered, the signature counter, the backup state) stays out of
            // the change set: the sign-in itself is recorded with its session.
            migrationBuilder.ProtectTenantTable("identity", "passkeys",
                ignore: ["last_used_at", "last_challenge_at", "sign_count", "backed_up"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UnprotectTenantTable("identity", "passkeys");
            migrationBuilder.DropTable(
                name: "passkeys",
                schema: "identity");
        }
    }
}
