using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingToken = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointments", x => x.Id);
                    table.CheckConstraint("CK_appointments_booking_token", "\"BookingToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_appointments_cancellation", "(\"Status\" = 'Cancelled' AND \"CancelledAtUtc\" IS NOT NULL) OR (\"Status\" <> 'Cancelled' AND \"CancelledAtUtc\" IS NULL AND \"CancellationReason\" IS NULL)");
                    table.CheckConstraint("CK_appointments_duration", "isfinite(\"StartAtUtc\") AND isfinite(\"EndAtUtc\") AND \"EndAtUtc\" > \"StartAtUtc\" AND \"DurationMinutes\" IN (15,30,45,60) AND \"EndAtUtc\" = \"StartAtUtc\" + \"DurationMinutes\" * INTERVAL '1 minute'");
                    table.CheckConstraint("CK_appointments_start_interval", "EXTRACT(MINUTE FROM (\"StartAtUtc\" AT TIME ZONE 'UTC'))::int % 15 = 0 AND EXTRACT(SECOND FROM (\"StartAtUtc\" AT TIME ZONE 'UTC')) = 0");
                    table.CheckConstraint("CK_appointments_status", "\"Status\" IN ('Scheduled','Completed','Cancelled','NoShow')");
                    table.ForeignKey(
                        name: "FK_appointments_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_appointments_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_BookingToken",
                table: "appointments",
                column: "BookingToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_appointments_DoctorId_StartAtUtc",
                table: "appointments",
                columns: new[] { "DoctorId", "StartAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_PatientId_StartAtUtc",
                table: "appointments",
                columns: new[] { "PatientId", "StartAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_StartAtUtc_Status",
                table: "appointments",
                columns: new[] { "StartAtUtc", "Status" });

            // PostgreSQL enforces half-open intervals even for simultaneous or
            // non-application writes. Adjacent appointments are allowed; cancelled
            // records retain their history without reserving the doctor's time.
            migrationBuilder.Sql("""
                ALTER TABLE appointments
                ADD CONSTRAINT "EX_appointments_doctor_time"
                EXCLUDE USING gist
                ("DoctorId" WITH =, tstzrange("StartAtUtc", "EndAtUtc", '[)') WITH &&)
                WHERE ("Status" <> 'Cancelled');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointments");

            // Retain btree_gist: other tables may use it by the time of rollback.
        }
    }
}
