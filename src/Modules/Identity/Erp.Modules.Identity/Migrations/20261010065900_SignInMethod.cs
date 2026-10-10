using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <summary>The sign-in history records how each attempt signed in: a password (the default the
    /// reviewed sign-in function's inserts take) or a passkey (critic p03 round 8). Attempts
    /// recorded before stay without a method: passkeys signed in from round 8 on, so they cannot be
    /// told apart; the default is set after the column is added so they are not labelled.</summary>
    public partial class SignInMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "method",
                schema: "identity",
                table: "sign_in_attempts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql("ALTER TABLE identity.sign_in_attempts ALTER COLUMN method SET DEFAULT 'password';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sign_in_attempts_method",
                schema: "identity",
                table: "sign_in_attempts",
                sql: "method IN ('password', 'passkey')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sign_in_attempts_method",
                schema: "identity",
                table: "sign_in_attempts");

            migrationBuilder.DropColumn(
                name: "method",
                schema: "identity",
                table: "sign_in_attempts");
        }
    }
}
