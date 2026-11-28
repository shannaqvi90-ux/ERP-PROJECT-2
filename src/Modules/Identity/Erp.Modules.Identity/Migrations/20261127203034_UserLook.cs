using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class UserLook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "density",
                schema: "identity",
                table: "users",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "theme",
                schema: "identity",
                table: "users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_density",
                schema: "identity",
                table: "users",
                sql: "density IS NULL OR density IN ('compact', 'comfortable')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_theme",
                schema: "identity",
                table: "users",
                sql: "theme IS NULL OR theme IN ('system', 'light', 'dark', 'hc')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_density",
                schema: "identity",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_theme",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "density",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "theme",
                schema: "identity",
                table: "users");
        }
    }
}
