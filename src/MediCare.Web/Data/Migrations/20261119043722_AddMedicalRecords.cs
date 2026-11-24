using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medical_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreationToken = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_records", x => x.Id);
                    table.CheckConstraint("CK_medical_records_token", "\"CreationToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_medical_records_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medical_records_appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medical_records_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medical_records_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medical_record_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Symptoms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Observations = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Diagnosis = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Treatment = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    AmendmentReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_record_revisions", x => x.Id);
                    table.CheckConstraint("CK_medical_record_revisions_final", "\"FinalizedAtUtc\" IS NULL OR (\"FinalizedAtUtc\" >= \"CreatedAtUtc\" AND length(btrim(\"Symptoms\")) > 0 AND length(btrim(\"Diagnosis\")) > 0 AND length(btrim(\"Treatment\")) > 0)");
                    table.CheckConstraint("CK_medical_record_revisions_number", "\"Number\" >= 1 AND ((\"Number\" = 1 AND \"AmendmentReason\" IS NULL) OR (\"Number\" > 1 AND length(btrim(\"AmendmentReason\")) > 0 AND \"AmendmentReason\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_medical_record_revisions_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medical_record_revisions_medical_records_MedicalRecordId",
                        column: x => x.MedicalRecordId,
                        principalTable: "medical_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medical_prescriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalRecordRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Medication = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Dosage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Route = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Frequency = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Duration = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_prescriptions", x => x.Id);
                    table.CheckConstraint("CK_medical_prescriptions_fields", "\"Position\" BETWEEN 0 AND 19 AND length(btrim(\"Medication\")) > 0 AND length(btrim(\"Dosage\")) > 0 AND length(btrim(\"Route\")) > 0 AND length(btrim(\"Frequency\")) > 0 AND length(btrim(\"Duration\")) > 0");
                    table.ForeignKey(
                        name: "FK_medical_prescriptions_medical_record_revisions_MedicalRecor~",
                        column: x => x.MedicalRecordRevisionId,
                        principalTable: "medical_record_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_medical_prescriptions_MedicalRecordRevisionId_Position",
                table: "medical_prescriptions",
                columns: new[] { "MedicalRecordRevisionId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medical_record_revisions_AuthorUserId",
                table: "medical_record_revisions",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_record_revisions_MedicalRecordId",
                table: "medical_record_revisions",
                column: "MedicalRecordId",
                unique: true,
                filter: "\"FinalizedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_medical_record_revisions_MedicalRecordId_Number",
                table: "medical_record_revisions",
                columns: new[] { "MedicalRecordId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medical_records_AppointmentId",
                table: "medical_records",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medical_records_AuthorUserId",
                table: "medical_records",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_records_CreationToken",
                table: "medical_records",
                column: "CreationToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medical_records_DoctorId_CreatedAtUtc",
                table: "medical_records",
                columns: new[] { "DoctorId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_records_PatientId_CreatedAtUtc",
                table: "medical_records",
                columns: new[] { "PatientId", "CreatedAtUtc" });

            // New EMR tables only. No existing clinical, appointment or Identity rows are changed.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.medicare_emr_record_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Medical encounter history cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF (to_jsonb(OLD) - 'Version') IS DISTINCT FROM (to_jsonb(NEW) - 'Version') THEN
                            RAISE EXCEPTION 'Encounter identity is immutable' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        PERFORM 1 FROM public.appointments a
                            JOIN public.doctors d ON d."Id" = a."DoctorId"
                            JOIN public.patients p ON p."Id" = a."PatientId"
                            WHERE a."Id" = NEW."AppointmentId" AND a."PatientId" = NEW."PatientId"
                            AND a."DoctorId" = NEW."DoctorId" AND d."IdentityUserId" = NEW."AuthorUserId"
                            AND d."IsActive" AND NOT p."IsArchived" AND a."Status" IN ('Scheduled','Completed')
                            FOR UPDATE OF a, d, p;
                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'Encounter care relationship is invalid' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER medicare_emr_record_guard BEFORE INSERT OR UPDATE OR DELETE ON public.medical_records
                    FOR EACH ROW EXECUTE FUNCTION public.medicare_emr_record_guard();

                CREATE FUNCTION public.medicare_emr_revision_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE owner_account text; previous_number integer; previous_final timestamptz;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Medical revision history cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF OLD."FinalizedAtUtc" IS NOT NULL THEN
                            RAISE EXCEPTION 'Finalized medical revisions are immutable' USING ERRCODE = '23514';
                        END IF;
                        IF ROW(NEW."Id",NEW."MedicalRecordId",NEW."Number",NEW."AuthorUserId",NEW."CreatedAtUtc")
                            IS DISTINCT FROM ROW(OLD."Id",OLD."MedicalRecordId",OLD."Number",OLD."AuthorUserId",OLD."CreatedAtUtc") THEN
                            RAISE EXCEPTION 'Revision identity is immutable' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        SELECT "AuthorUserId" INTO owner_account FROM public.medical_records WHERE "Id" = NEW."MedicalRecordId" FOR UPDATE;
                        IF NOT FOUND OR owner_account IS DISTINCT FROM NEW."AuthorUserId" THEN
                            RAISE EXCEPTION 'Revision author is invalid' USING ERRCODE = '23514';
                        END IF;
                        SELECT "Number", "FinalizedAtUtc" INTO previous_number, previous_final
                            FROM public.medical_record_revisions WHERE "MedicalRecordId" = NEW."MedicalRecordId" ORDER BY "Number" DESC LIMIT 1;
                        IF NEW."Number" <> COALESCE(previous_number, 0) + 1 OR (previous_number IS NOT NULL AND previous_final IS NULL) OR NEW."FinalizedAtUtc" IS NOT NULL THEN
                            RAISE EXCEPTION 'A new sequential draft revision is required' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER medicare_emr_revision_guard BEFORE INSERT OR UPDATE OR DELETE ON public.medical_record_revisions
                    FOR EACH ROW EXECUTE FUNCTION public.medicare_emr_revision_guard();

                CREATE FUNCTION public.medicare_emr_prescription_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE revision_id uuid; finalized timestamptz;
                BEGIN
                    IF TG_OP = 'UPDATE' AND ROW(NEW."Id",NEW."MedicalRecordRevisionId") IS DISTINCT FROM ROW(OLD."Id",OLD."MedicalRecordRevisionId") THEN
                        RAISE EXCEPTION 'Prescription identity is immutable' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'DELETE' THEN revision_id := OLD."MedicalRecordRevisionId"; ELSE revision_id := NEW."MedicalRecordRevisionId"; END IF;
                    SELECT "FinalizedAtUtc" INTO finalized FROM public.medical_record_revisions WHERE "Id" = revision_id FOR UPDATE;
                    IF NOT FOUND OR finalized IS NOT NULL THEN
                        RAISE EXCEPTION 'Prescriptions require an editable draft revision' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
                END; $$;
                CREATE TRIGGER medicare_emr_prescription_guard BEFORE INSERT OR UPDATE OR DELETE ON public.medical_prescriptions
                    FOR EACH ROW EXECUTE FUNCTION public.medicare_emr_prescription_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medical_prescriptions");

            migrationBuilder.DropTable(
                name: "medical_record_revisions");

            migrationBuilder.DropTable(
                name: "medical_records");

            migrationBuilder.Sql("""
                DROP FUNCTION public.medicare_emr_prescription_guard();
                DROP FUNCTION public.medicare_emr_revision_guard();
                DROP FUNCTION public.medicare_emr_record_guard();
                """);
        }
    }
}
