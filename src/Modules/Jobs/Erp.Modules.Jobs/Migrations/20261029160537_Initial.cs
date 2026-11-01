using System;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Jobs.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "jobs");

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(72)", maxLength: 72, nullable: false),
                    title_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    title_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    source = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    progress_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    progress_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    error_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    error_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    error_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    output_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    output_content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    output_text = table.Column<string>(type: "text", nullable: true),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rerun_of = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.UniqueConstraint("ak_jobs_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_jobs_attempts", "attempts >= 0 AND max_attempts BETWEEN 1 AND 10 AND attempts <= max_attempts");
                    table.CheckConstraint("ck_jobs_priority", "priority BETWEEN -10 AND 10");
                    table.CheckConstraint("ck_jobs_progress", "progress BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_jobs_source", "source IN ('user', 'scheduler', 'module', 'rerun')");
                    table.CheckConstraint("ck_jobs_status", "status IN ('queued', 'running', 'succeeded', 'failed', 'cancelled')");
                    table.CheckConstraint("ck_jobs_type", "type ~ '^[a-z][a-zA-Z0-9]+\\.[a-z][a-zA-Z0-9]+$'");
                });

            migrationBuilder.CreateTable(
                name: "schedules",
                schema: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    type = table.Column<string>(type: "character varying(72)", maxLength: 72, nullable: false),
                    cron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedules", x => x.id);
                    table.UniqueConstraint("ak_schedules_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_schedules_type", "type ~ '^[a-z][a-zA-Z0-9]+\\.[a-z][a-zA-Z0-9]+$'");
                });

            migrationBuilder.CreateTable(
                name: "job_attempts",
                schema: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    error_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    error_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    error_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    worker_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_attempts", x => x.id);
                    table.UniqueConstraint("ak_job_attempts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_job_attempts_number", "number BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_job_attempts_outcome", "outcome IN ('running', 'succeeded', 'failed', 'cancelled', 'timedOut', 'abandoned')");
                    table.ForeignKey(
                        name: "fk_job_attempts_jobs_tenant_id_job_id",
                        columns: x => new { x.tenant_id, x.job_id },
                        principalSchema: "jobs",
                        principalTable: "jobs",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_attempts_tenant_id_job_id_number",
                schema: "jobs",
                table: "job_attempts",
                columns: new[] { "tenant_id", "job_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_attempts_id",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "attempts", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_created_at_id",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_finished_at_id",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "finished_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_progress_id",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "progress", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_run_at_id",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "run_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_schedule_id",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "schedule_id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_started_at_id",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "started_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_status_id",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_status_run_at",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "status", "run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_title_en_id",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "title_en", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_tenant_id_type_id",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "tenant_id", "type", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_title_en_title_ar_type",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "title_en", "title_ar", "type" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_schedules_name_type",
                schema: "jobs",
                table: "schedules",
                columns: new[] { "name", "type" })
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops", "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_schedules_tenant_id_cron_id",
                schema: "jobs",
                table: "schedules",
                columns: new[] { "tenant_id", "cron", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_schedules_tenant_id_is_enabled_id",
                schema: "jobs",
                table: "schedules",
                columns: new[] { "tenant_id", "is_enabled", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_schedules_tenant_id_is_enabled_next_run_at",
                schema: "jobs",
                table: "schedules",
                columns: new[] { "tenant_id", "is_enabled", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_schedules_tenant_id_last_run_at_id",
                schema: "jobs",
                table: "schedules",
                columns: new[] { "tenant_id", "last_run_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_schedules_tenant_id_name",
                schema: "jobs",
                table: "schedules",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schedules_tenant_id_name_id",
                schema: "jobs",
                table: "schedules",
                columns: new[] { "tenant_id", "name", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_schedules_tenant_id_next_run_at_id",
                schema: "jobs",
                table: "schedules",
                columns: new[] { "tenant_id", "next_run_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_schedules_tenant_id_type_id",
                schema: "jobs",
                table: "schedules",
                columns: new[] { "tenant_id", "type", "id" });

            // Row-level security on every table (forced, for the owner too), the application role's
            // grants and the audit trigger. A running job's progress and lease change often and are
            // left out of the audit trail; its state, tries, errors, cancel requests and results are in it.
            migrationBuilder.GrantSchemaUsage("jobs");
            migrationBuilder.ProtectTenantTable("jobs", "jobs", ignore: ["progress", "progress_en", "progress_ar", "locked_until", "locked_by"]);
            migrationBuilder.ProtectTenantTable("jobs", "job_attempts");
            migrationBuilder.ProtectTenantTable("jobs", "schedules");
            migrationBuilder.Sql(JobsSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(JobsSql.Down);
            migrationBuilder.UnprotectTenantTable("jobs", "schedules");
            migrationBuilder.UnprotectTenantTable("jobs", "job_attempts");
            migrationBuilder.UnprotectTenantTable("jobs", "jobs");
            migrationBuilder.DropTable(
                name: "job_attempts",
                schema: "jobs");

            migrationBuilder.DropTable(
                name: "schedules",
                schema: "jobs");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "jobs");
        }
    }
}
