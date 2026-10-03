using System;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class CredentialsAndSignInHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sign_in_unblocked_at",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sign_in_attempts",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_in_attempts", x => x.id);
                    table.CheckConstraint("ck_sign_in_attempts_outcome", "outcome IN ('succeeded', 'failed', 'throttled', 'inactive', 'expired')");
                    table.ForeignKey(
                        name: "fk_sign_in_attempts_users_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_credentials",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    must_change = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_credentials", x => x.id);
                    table.CheckConstraint("ck_user_credentials_expiry", "expires_at IS NULL OR must_change");
                    table.ForeignKey(
                        name: "fk_user_credentials_users_tenant_id_id",
                        columns: x => new { x.tenant_id, x.id },
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sign_in_attempts_tenant_id_user_id_occurred_at",
                schema: "identity",
                table: "sign_in_attempts",
                columns: new[] { "tenant_id", "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sign_in_attempts_user_id_source_occurred_at",
                schema: "identity",
                table: "sign_in_attempts",
                columns: new[] { "user_id", "source", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_credentials_tenant_id_id",
                schema: "identity",
                table: "user_credentials",
                columns: new[] { "tenant_id", "id" },
                unique: true);

            migrationBuilder.ProtectTenantTable("identity", "user_credentials", redact: ["password_hash"]);
            migrationBuilder.ProtectTenantTable("identity", "sign_in_attempts", audited: false);
            migrationBuilder.Sql(IdentitySql.CredentialsUp);

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_failed_sign_in_count",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "failed_sign_in_count",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lockout_until",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_hash",
                schema: "identity",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failed_sign_in_count",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lockout_until",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                schema: "identity",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_failed_sign_in_count",
                schema: "identity",
                table: "users",
                sql: "failed_sign_in_count >= 0");

            migrationBuilder.Sql(IdentitySql.CredentialsDown);

            migrationBuilder.DropTable(
                name: "sign_in_attempts",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_credentials",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "sign_in_unblocked_at",
                schema: "identity",
                table: "users");
        }
    }
}
