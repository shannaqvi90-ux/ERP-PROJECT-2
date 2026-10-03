using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class ListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_users_display_name_email_normalized",
                schema: "identity",
                table: "users",
                columns: new[] { "display_name", "email_normalized" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops", "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_created_at_id",
                schema: "identity",
                table: "users",
                columns: new[] { "tenant_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_last_sign_in_at_id",
                schema: "identity",
                table: "users",
                columns: new[] { "tenant_id", "last_sign_in_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_display_name_email_normalized",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_created_at_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_last_sign_in_at_id",
                schema: "identity",
                table: "users");
        }
    }
}
