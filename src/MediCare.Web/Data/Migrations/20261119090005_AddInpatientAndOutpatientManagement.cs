using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInpatientAndOutpatientManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationToken = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AdmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AdmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ClearedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClearedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DischargedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DischargedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admissions", x => x.Id);
                    table.CheckConstraint("CK_admissions_state", "(\"Status\"='Admitted' AND \"ClearedAtUtc\" IS NULL AND \"DischargedAtUtc\" IS NULL) OR (\"Status\"='DischargeReady' AND \"ClearedAtUtc\" IS NOT NULL AND \"DischargedAtUtc\" IS NULL) OR (\"Status\"='Discharged' AND \"ClearedAtUtc\" IS NOT NULL AND \"DischargedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_admissions_times", "isfinite(\"AdmittedAtUtc\") AND (\"ClearedAtUtc\" IS NULL OR (isfinite(\"ClearedAtUtc\") AND \"ClearedAtUtc\" >= \"AdmittedAtUtc\")) AND (\"DischargedAtUtc\" IS NULL OR (isfinite(\"DischargedAtUtc\") AND \"DischargedAtUtc\" >= \"ClearedAtUtc\")) AND (\"ClearedAtUtc\" IS NULL) = (\"ClearedByUserId\" IS NULL) AND (\"DischargedAtUtc\" IS NULL) = (\"DischargedByUserId\" IS NULL)");
                    table.CheckConstraint("CK_admissions_token", "\"CreationToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_admissions_AspNetUsers_AdmittedByUserId",
                        column: x => x.AdmittedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admissions_AspNetUsers_ClearedByUserId",
                        column: x => x.ClearedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admissions_AspNetUsers_DischargedByUserId",
                        column: x => x.DischargedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admissions_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admissions_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admissions_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outpatient_visits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationToken = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegisteredByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CompletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outpatient_visits", x => x.Id);
                    table.CheckConstraint("CK_outpatient_visits_actors", "(\"StartedAtUtc\" IS NULL) = (\"StartedByUserId\" IS NULL) AND (\"CompletedAtUtc\" IS NULL) = (\"CompletedByUserId\" IS NULL) AND (\"CancelledAtUtc\" IS NULL) = (\"CancelledByUserId\" IS NULL) AND ((\"Status\"='Cancelled' AND \"CancellationReason\" IS NOT NULL AND length(btrim(\"CancellationReason\")) > 0) OR (\"Status\"<>'Cancelled' AND \"CancellationReason\" IS NULL))");
                    table.CheckConstraint("CK_outpatient_visits_state", "(\"Status\"='Registered' AND \"StartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL) OR (\"Status\"='InConsultation' AND \"StartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL) OR (\"Status\"='Completed' AND \"StartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL) OR (\"Status\"='Cancelled' AND \"StartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_outpatient_visits_times", "isfinite(\"RegisteredAtUtc\") AND (\"StartedAtUtc\" IS NULL OR (isfinite(\"StartedAtUtc\") AND \"StartedAtUtc\" >= \"RegisteredAtUtc\")) AND (\"CompletedAtUtc\" IS NULL OR (isfinite(\"CompletedAtUtc\") AND \"CompletedAtUtc\" >= \"StartedAtUtc\")) AND (\"CancelledAtUtc\" IS NULL OR (isfinite(\"CancelledAtUtc\") AND \"CancelledAtUtc\" >= \"RegisteredAtUtc\"))");
                    table.CheckConstraint("CK_outpatient_visits_token", "\"CreationToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_outpatient_visits_AspNetUsers_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outpatient_visits_AspNetUsers_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outpatient_visits_AspNetUsers_RegisteredByUserId",
                        column: x => x.RegisteredByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outpatient_visits_AspNetUsers_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outpatient_visits_appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outpatient_visits_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outpatient_visits_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wards", x => x.Id);
                    table.CheckConstraint("CK_wards_fields", "length(btrim(\"Code\")) > 0 AND \"Code\" = upper(btrim(\"Code\")) AND length(btrim(\"Name\")) > 0");
                    table.ForeignKey(
                        name: "FK_wards_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admission_clinical_notes",
                columns: table => new
                {
                    AdmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmissionReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DischargeSummary = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    MedicalRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admission_clinical_notes", x => x.AdmissionId);
                    table.ForeignKey(
                        name: "FK_admission_clinical_notes_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admission_clinical_notes_admissions_AdmissionId",
                        column: x => x.AdmissionId,
                        principalTable: "admissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_admission_clinical_notes_medical_records_MedicalRecordId",
                        column: x => x.MedicalRecordId,
                        principalTable: "medical_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outpatient_clinical_notes",
                columns: table => new
                {
                    OutpatientVisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    FollowUpInformation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FollowUpOn = table.Column<DateOnly>(type: "date", nullable: true),
                    AuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outpatient_clinical_notes", x => x.OutpatientVisitId);
                    table.ForeignKey(
                        name: "FK_outpatient_clinical_notes_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outpatient_clinical_notes_medical_records_MedicalRecordId",
                        column: x => x.MedicalRecordId,
                        principalTable: "medical_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outpatient_clinical_notes_outpatient_visits_OutpatientVisit~",
                        column: x => x.OutpatientVisitId,
                        principalTable: "outpatient_visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "beds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsOperational = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_beds", x => x.Id);
                    table.CheckConstraint("CK_beds_number", "length(btrim(\"Number\")) > 0 AND \"Number\" = upper(btrim(\"Number\"))");
                    table.ForeignKey(
                        name: "FK_beds_wards_WardId",
                        column: x => x.WardId,
                        principalTable: "wards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bed_occupancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BedId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bed_occupancies", x => x.Id);
                    table.CheckConstraint("CK_bed_occupancies_times", "isfinite(\"StartedAtUtc\") AND (\"EndedAtUtc\" IS NULL OR (isfinite(\"EndedAtUtc\") AND \"EndedAtUtc\" >= \"StartedAtUtc\"))");
                    table.ForeignKey(
                        name: "FK_bed_occupancies_admissions_AdmissionId",
                        column: x => x.AdmissionId,
                        principalTable: "admissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bed_occupancies_beds_BedId",
                        column: x => x.BedId,
                        principalTable: "beds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bed_transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromBedId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToBedId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bed_transfers", x => x.Id);
                    table.CheckConstraint("CK_bed_transfers_fields", "\"Token\" <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(\"OccurredAtUtc\") AND length(btrim(\"Reason\")) > 0 AND (\"FromBedId\" IS NOT NULL OR \"ToBedId\" IS NOT NULL) AND \"FromBedId\" IS DISTINCT FROM \"ToBedId\"");
                    table.ForeignKey(
                        name: "FK_bed_transfers_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bed_transfers_admissions_AdmissionId",
                        column: x => x.AdmissionId,
                        principalTable: "admissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bed_transfers_beds_FromBedId",
                        column: x => x.FromBedId,
                        principalTable: "beds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bed_transfers_beds_ToBedId",
                        column: x => x.ToBedId,
                        principalTable: "beds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admission_clinical_notes_AuthorUserId",
                table: "admission_clinical_notes",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_admission_clinical_notes_MedicalRecordId",
                table: "admission_clinical_notes",
                column: "MedicalRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_admissions_AdmittedByUserId",
                table: "admissions",
                column: "AdmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_admissions_ClearedByUserId",
                table: "admissions",
                column: "ClearedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_admissions_CreationToken",
                table: "admissions",
                column: "CreationToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_admissions_DepartmentId_Status",
                table: "admissions",
                columns: new[] { "DepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_admissions_DischargedByUserId",
                table: "admissions",
                column: "DischargedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_admissions_DoctorId_AdmittedAtUtc",
                table: "admissions",
                columns: new[] { "DoctorId", "AdmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_admissions_PatientId",
                table: "admissions",
                column: "PatientId",
                unique: true,
                filter: "\"Status\" <> 'Discharged'");

            migrationBuilder.CreateIndex(
                name: "IX_bed_occupancies_AdmissionId",
                table: "bed_occupancies",
                column: "AdmissionId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bed_occupancies_BedId",
                table: "bed_occupancies",
                column: "BedId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bed_transfers_ActorUserId",
                table: "bed_transfers",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_bed_transfers_AdmissionId_OccurredAtUtc",
                table: "bed_transfers",
                columns: new[] { "AdmissionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_bed_transfers_FromBedId",
                table: "bed_transfers",
                column: "FromBedId");

            migrationBuilder.CreateIndex(
                name: "IX_bed_transfers_ToBedId",
                table: "bed_transfers",
                column: "ToBedId");

            migrationBuilder.CreateIndex(
                name: "IX_bed_transfers_Token",
                table: "bed_transfers",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_beds_WardId_Number",
                table: "beds",
                columns: new[] { "WardId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outpatient_clinical_notes_AuthorUserId",
                table: "outpatient_clinical_notes",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_outpatient_clinical_notes_MedicalRecordId",
                table: "outpatient_clinical_notes",
                column: "MedicalRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_outpatient_visits_AppointmentId",
                table: "outpatient_visits",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outpatient_visits_CancelledByUserId",
                table: "outpatient_visits",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_outpatient_visits_CompletedByUserId",
                table: "outpatient_visits",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_outpatient_visits_CreationToken",
                table: "outpatient_visits",
                column: "CreationToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outpatient_visits_DoctorId_Status_RegisteredAtUtc",
                table: "outpatient_visits",
                columns: new[] { "DoctorId", "Status", "RegisteredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_outpatient_visits_PatientId_RegisteredAtUtc",
                table: "outpatient_visits",
                columns: new[] { "PatientId", "RegisteredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_outpatient_visits_RegisteredByUserId",
                table: "outpatient_visits",
                column: "RegisteredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_outpatient_visits_StartedByUserId",
                table: "outpatient_visits",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_wards_Code",
                table: "wards",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wards_DepartmentId",
                table: "wards",
                column: "DepartmentId");
            migrationBuilder.Sql(HospitalStayDatabaseSql.Guards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Stay history rollback requires a separately reviewed data-preserving procedure.");
        }
    }
}
