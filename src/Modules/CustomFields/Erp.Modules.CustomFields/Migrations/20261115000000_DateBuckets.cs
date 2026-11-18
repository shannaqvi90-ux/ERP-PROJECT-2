using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.CustomFields.Migrations
{
    /// <summary>
    /// A date field's value cut to the first day of its month or year (<c>customfields.value_date_trunc</c>),
    /// so a list can group records by a date field per month or year. Runs as the caller like the other
    /// value functions: it reads through <c>customfields.value_date</c>, so row-level security applies.
    /// </summary>
    [DbContext(typeof(CustomFieldsDbContext))]
    [Migration("20261115000000_DateBuckets")]
    public partial class DateBuckets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE FUNCTION customfields.value_date_trunc(field uuid, record uuid, unit text) RETURNS date
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT CASE unit
        WHEN 'month' THEN make_date(extract(year FROM d)::int, extract(month FROM d)::int, 1)
        WHEN 'year' THEN make_date(extract(year FROM d)::int, 1, 1)
        ELSE d END
    FROM (SELECT customfields.value_date(field, record) AS d) x
$$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION customfields.value_date_trunc(uuid, uuid, text);");
        }
    }
}
