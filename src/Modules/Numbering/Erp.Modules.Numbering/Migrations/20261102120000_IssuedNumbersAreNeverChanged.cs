using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Numbering.Migrations
{
    /// <summary>
    /// Owner rule 2 of the numbering decision (#32): a number once given is never changed or removed.
    /// The application role loses UPDATE and DELETE on the table of given numbers (it still reads and
    /// inserts); the database, not only the code, refuses a change.
    /// </summary>
    [DbContext(typeof(NumberingDbContext))]
    [Migration("20261102120000_IssuedNumbersAreNeverChanged")]
    public partial class IssuedNumbersAreNeverChanged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON numbering.issued_numbers FROM erp_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON numbering.issued_numbers TO erp_app;");
        }
    }
}
