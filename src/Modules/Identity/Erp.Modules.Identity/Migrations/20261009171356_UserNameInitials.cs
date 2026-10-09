using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <summary>The users' initials, kept by the database from the display name and indexed with the
    /// tenant, so a one-word search of the users list also finds "map" for Majid Anil Pillai.</summary>
    public partial class UserNameInitials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name_initials",
                schema: "identity",
                table: "users",
                type: "text",
                nullable: false,
                computedColumnSql: "regexp_replace(regexp_replace(lower(btrim(display_name)), '([^[:space:]-])[^[:space:]-]*[[:space:]-]*', '\\1', 'g'), '[^[:alpha:]]+', '', 'g')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_name_initials",
                schema: "identity",
                table: "users",
                columns: new[] { "tenant_id", "name_initials" });

            migrationBuilder.Sql(IdentitySql.NameInitials);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentitySql.NameInitialsDown);

            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_name_initials",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "name_initials",
                schema: "identity",
                table: "users");
        }
    }
}
