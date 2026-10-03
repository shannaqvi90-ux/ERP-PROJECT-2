using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class UserNumerals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "numerals",
                schema: "identity",
                table: "users",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "latn");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_numerals",
                schema: "identity",
                table: "users",
                sql: "numerals IN ('latn', 'arab')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_numerals",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "numerals",
                schema: "identity",
                table: "users");
        }
    }
}
