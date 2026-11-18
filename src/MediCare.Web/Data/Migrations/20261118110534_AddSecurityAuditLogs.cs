using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "security_audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    TargetUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_logs_OccurredAtUtc",
                table: "security_audit_logs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_logs_TargetUserId",
                table: "security_audit_logs",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "security_audit_logs");
        }
    }
}
