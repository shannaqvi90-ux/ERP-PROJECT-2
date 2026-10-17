using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <summary>Sign-in says when it is paused, for every address alike (critic p00 rounds 7 to 10,
    /// routed to p03): failed attempts are counted by keyed address and client in the platform table
    /// identity.sign_in_failures, and the reviewed sign-in function answers the moment a pause lifts
    /// (<see cref="IdentitySql.PauseByAddressUp"/>). No change to the EF model.</summary>
    public partial class SignInPauseByAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentitySql.PauseByAddressUp);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentitySql.PauseByAddressDown);
        }
    }
}
