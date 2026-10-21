using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Migrations
{
    /// <summary>A device the user or an administrator lets in signs in from a shared address while
    /// sign-in there is paused (critic p03 round 12): the approval on the user (the SHA-256 of the
    /// device code and until when) and the reviewed sign-in function's <c>p_pending</c> and
    /// <c>p_approval</c> (<see cref="IdentitySql.DeviceApprovalUp"/>).</summary>
    public partial class DeviceApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "device_approval_hash",
                schema: "identity",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "device_approval_until",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_device_approval",
                schema: "identity",
                table: "users",
                sql: "(device_approval_hash IS NULL AND device_approval_until IS NULL) OR (length(device_approval_hash) = 32 AND device_approval_until IS NOT NULL)");

            migrationBuilder.Sql(IdentitySql.DeviceApprovalUp);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IdentitySql.DeviceApprovalDown);

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_device_approval",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "device_approval_hash",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "device_approval_until",
                schema: "identity",
                table: "users");
        }
    }
}
