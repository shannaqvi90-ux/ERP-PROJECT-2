using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.ImportExport.Migrations
{
    /// <inheritdoc />
    public partial class DuplicateHandling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "on_duplicate",
                schema: "importexport",
                table: "import_sessions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "create");

            migrationBuilder.AddColumn<int>(
                name: "rows_duplicate",
                schema: "importexport",
                table: "import_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rows_updated",
                schema: "importexport",
                table: "import_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_import_sessions_on_duplicate",
                schema: "importexport",
                table: "import_sessions",
                sql: "on_duplicate IN ('create', 'skip', 'update') AND rows_duplicate >= 0 AND rows_updated >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_import_sessions_on_duplicate",
                schema: "importexport",
                table: "import_sessions");

            migrationBuilder.DropColumn(
                name: "on_duplicate",
                schema: "importexport",
                table: "import_sessions");

            migrationBuilder.DropColumn(
                name: "rows_duplicate",
                schema: "importexport",
                table: "import_sessions");

            migrationBuilder.DropColumn(
                name: "rows_updated",
                schema: "importexport",
                table: "import_sessions");
        }
    }
}
