using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Numbering.Migrations
{
    /// <inheritdoc />
    public partial class AuditNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "numbering",
                table: "sequences",
                type: "character varying(72)",
                maxLength: 72,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name_ar",
                schema: "numbering",
                table: "sequences",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name_en",
                schema: "numbering",
                table: "sequences",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "next_set_from",
                schema: "numbering",
                table: "sequences",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "next_set_period",
                schema: "numbering",
                table: "sequences",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "next_set_to",
                schema: "numbering",
                table: "sequences",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "numbering",
                table: "issued_numbers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "numbering",
                table: "counters",
                type: "character varying(72)",
                maxLength: 72,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name_ar",
                schema: "numbering",
                table: "counters",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name_en",
                schema: "numbering",
                table: "counters",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
            // The audit trail names a changed record from its name_en, name_ar and code columns. They are
            // names, not business data: the trigger leaves them out of the recorded changes.
            foreach (var table in new[] { "sequences", "counters", "issued_numbers" })
            {
                migrationBuilder.Sql($"DROP TRIGGER audit_capture ON numbering.{table};");
            }
            // A given number is its own record's name; a counter is named as its sequence.
            migrationBuilder.Sql("UPDATE numbering.issued_numbers SET code = number;");
            migrationBuilder.Sql("""
                CREATE FUNCTION numbering.name_issued_number() RETURNS trigger
                    LANGUAGE plpgsql
                    SET search_path = pg_catalog, pg_temp
                AS $$
                BEGIN
                    NEW.code := NEW.number;
                    RETURN NEW;
                END
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION numbering.name_counter() RETURNS trigger
                    LANGUAGE plpgsql
                    SET search_path = pg_catalog, pg_temp
                AS $$
                BEGIN
                    SELECT s.name_en, s.name_ar, s.code INTO NEW.name_en, NEW.name_ar, NEW.code
                      FROM numbering.sequences s WHERE s.id = NEW.sequence_id AND s.tenant_id = NEW.tenant_id;
                    RETURN NEW;
                END
                $$;
                """);
            migrationBuilder.Sql("CREATE TRIGGER name_before_write BEFORE INSERT ON numbering.issued_numbers FOR EACH ROW EXECUTE FUNCTION numbering.name_issued_number();");
            migrationBuilder.Sql("CREATE TRIGGER name_before_write BEFORE INSERT OR UPDATE ON numbering.counters FOR EACH ROW EXECUTE FUNCTION numbering.name_counter();");
            migrationBuilder.Sql("GRANT EXECUTE ON FUNCTION numbering.name_issued_number(), numbering.name_counter() TO erp_app;");
            foreach (var table in new[] { "sequences", "counters", "issued_numbers" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON numbering.{table} " +
                                     "FOR EACH ROW EXECUTE FUNCTION audit.capture('-name_en', '-name_ar', '-code');");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "sequences", "counters", "issued_numbers" })
            {
                migrationBuilder.Sql($"DROP TRIGGER audit_capture ON numbering.{table};");
            }
            migrationBuilder.Sql("DROP TRIGGER name_before_write ON numbering.issued_numbers;");
            migrationBuilder.Sql("DROP TRIGGER name_before_write ON numbering.counters;");
            migrationBuilder.Sql("DROP FUNCTION numbering.name_issued_number();");
            migrationBuilder.Sql("DROP FUNCTION numbering.name_counter();");
            migrationBuilder.DropColumn(
                name: "code",
                schema: "numbering",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "name_ar",
                schema: "numbering",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "name_en",
                schema: "numbering",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "next_set_from",
                schema: "numbering",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "next_set_period",
                schema: "numbering",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "next_set_to",
                schema: "numbering",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "numbering",
                table: "issued_numbers");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "numbering",
                table: "counters");

            migrationBuilder.DropColumn(
                name: "name_ar",
                schema: "numbering",
                table: "counters");

            migrationBuilder.DropColumn(
                name: "name_en",
                schema: "numbering",
                table: "counters");

            foreach (var table in new[] { "sequences", "counters", "issued_numbers" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON numbering.{table} FOR EACH ROW EXECUTE FUNCTION audit.capture();");
            }
        }
    }
}
