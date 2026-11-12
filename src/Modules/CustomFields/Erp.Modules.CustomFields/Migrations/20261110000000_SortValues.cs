using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.CustomFields.Migrations
{
    /// <summary>
    /// A record's value of a field as a scalar (<c>customfields.value_*</c>), so a list can order and
    /// group by a custom field (Erp.Kernel.CustomFields.CustomFieldSql). Each runs as the caller and
    /// reads one row through the unique index on (tenant, record, field), so row-level security keeps
    /// the answer inside the caller's workspace.
    /// </summary>
    [DbContext(typeof(CustomFieldsDbContext))]
    [Migration("20261110000000_SortValues")]
    public partial class SortValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, column, type) in new[]
            {
                ("value_number", "number_value", "numeric"), ("value_date", "date_value", "date"),
                ("value_bool", "bool_value", "boolean"), ("value_text", "text_value", "text"),
            })
            {
                migrationBuilder.Sql($@"
CREATE FUNCTION customfields.{name}(field uuid, record uuid) RETURNS {type}
LANGUAGE sql STABLE PARALLEL SAFE AS $$
    SELECT (SELECT v.{column} FROM customfields.field_values v
             WHERE v.tenant_id = erp.current_tenant_id() AND v.record_id = record AND v.field_id = field)
$$;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION customfields.value_number(uuid, uuid); DROP FUNCTION customfields.value_date(uuid, uuid); DROP FUNCTION customfields.value_bool(uuid, uuid); DROP FUNCTION customfields.value_text(uuid, uuid);");
        }
    }
}
