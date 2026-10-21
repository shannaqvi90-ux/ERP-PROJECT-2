using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <summary>A valid set-up code still signs in from a shared address (a container gateway, a
    /// proxy that does not forward the client's address) while sign-in from that address is paused
    /// (critic p03 round 11): the reviewed sign-in function gains <c>p_shared</c>
    /// (<see cref="IdentitySql.SetupCodeFromSharedAddressUp"/>). No change to the EF model.</summary>
    public partial class SetupCodeFromSharedAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentitySql.SetupCodeFromSharedAddressUp);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentitySql.SetupCodeFromSharedAddressDown);
        }
    }
}
