using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLaboratoryManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lab_tests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SampleType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResultKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Unit = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ReferenceRange = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_tests", x => x.Id);
                    table.CheckConstraint("CK_lab_tests_kind", "\"ResultKind\" IN ('Numeric','Text')");
                    table.CheckConstraint("CK_lab_tests_required", "\"Code\" ~ '^[A-Z0-9][A-Z0-9_.-]{0,39}$' AND length(btrim(\"Name\")) > 0 AND length(btrim(\"Category\")) > 0 AND length(btrim(\"SampleType\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "lab_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationToken = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    LabTestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    TestCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TestName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SampleType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResultKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Unit = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ReferenceRange = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SampleIdentifier = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CollectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CollectedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ProcessingAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_requests", x => x.Id);
                    table.CheckConstraint("CK_lab_requests_cancellation", "(\"Status\" <> 'Cancelled' AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL AND \"CancellationReason\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"CancelledAtUtc\" IS NOT NULL AND \"CancelledAtUtc\" >= \"RequestedAtUtc\" AND \"CancelledByUserId\" IS NOT NULL AND length(btrim(\"CancellationReason\")) > 0 AND \"CancellationReason\" IS NOT NULL)");
                    table.CheckConstraint("CK_lab_requests_completion", "(\"CompletedAtUtc\" IS NULL AND \"Status\" <> 'Completed') OR (\"CompletedAtUtc\" IS NOT NULL AND \"Status\" = 'Completed' AND \"ProcessingAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" >= \"ProcessingAtUtc\")");
                    table.CheckConstraint("CK_lab_requests_kind", "\"ResultKind\" IN ('Numeric','Text')");
                    table.CheckConstraint("CK_lab_requests_processing", "(\"ProcessingAtUtc\" IS NULL AND \"ProcessingByUserId\" IS NULL) OR (\"ProcessingAtUtc\" IS NOT NULL AND \"ProcessingByUserId\" IS NOT NULL AND \"ReceivedAtUtc\" IS NOT NULL AND \"ProcessingAtUtc\" >= \"ReceivedAtUtc\")");
                    table.CheckConstraint("CK_lab_requests_receipt", "(\"ReceivedAtUtc\" IS NULL AND \"ReceivedByUserId\" IS NULL) OR (\"ReceivedAtUtc\" IS NOT NULL AND \"ReceivedByUserId\" IS NOT NULL AND \"CollectedAtUtc\" IS NOT NULL AND \"ReceivedAtUtc\" >= \"CollectedAtUtc\")");
                    table.CheckConstraint("CK_lab_requests_sample", "(\"SampleIdentifier\" IS NULL AND \"CollectedAtUtc\" IS NULL AND \"CollectedByUserId\" IS NULL) OR (\"SampleIdentifier\" IS NOT NULL AND \"SampleIdentifier\" ~ '^LAB-[A-F0-9]{32}$' AND \"CollectedAtUtc\" >= \"RequestedAtUtc\" AND \"CollectedAtUtc\" IS NOT NULL AND \"CollectedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_lab_requests_stage", "(\"Status\" IN ('Requested','Cancelled') OR \"CollectedAtUtc\" IS NOT NULL) AND (\"Status\" NOT IN ('InProgress','Completed') OR \"ProcessingAtUtc\" IS NOT NULL) AND (\"Status\" <> 'Requested' OR \"CollectedAtUtc\" IS NULL) AND (\"Status\" <> 'SampleCollected' OR \"ProcessingAtUtc\" IS NULL)");
                    table.CheckConstraint("CK_lab_requests_status", "\"Status\" IN ('Requested','SampleCollected','InProgress','Completed','Cancelled')");
                    table.CheckConstraint("CK_lab_requests_token", "\"CreationToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_lab_requests_AspNetUsers_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lab_requests_AspNetUsers_CollectedByUserId",
                        column: x => x.CollectedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lab_requests_AspNetUsers_ProcessingByUserId",
                        column: x => x.ProcessingByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lab_requests_AspNetUsers_ReceivedByUserId",
                        column: x => x.ReceivedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lab_requests_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lab_requests_lab_tests_LabTestId",
                        column: x => x.LabTestId,
                        principalTable: "lab_tests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lab_requests_medical_records_MedicalRecordId",
                        column: x => x.MedicalRecordId,
                        principalTable: "medical_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lab_result_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LabRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationToken = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    NumericValue = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    TextValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Comments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_result_revisions", x => x.Id);
                    table.CheckConstraint("CK_lab_result_revisions_number", "\"Number\" >= 1 AND ((\"Number\" = 1 AND \"CorrectionReason\" IS NULL) OR (\"Number\" > 1 AND \"CorrectionReason\" IS NOT NULL AND length(btrim(\"CorrectionReason\")) > 0))");
                    table.CheckConstraint("CK_lab_result_revisions_token", "\"CreationToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_lab_result_revisions_value", "(\"NumericValue\" IS NULL OR \"NumericValue\" <> 'NaN'::numeric) AND NOT (\"NumericValue\" IS NOT NULL AND \"TextValue\" IS NOT NULL) AND (\"FinalizedAtUtc\" IS NULL OR (\"FinalizedAtUtc\" >= \"CreatedAtUtc\" AND (\"NumericValue\" IS NOT NULL OR (\"TextValue\" IS NOT NULL AND length(btrim(\"TextValue\")) > 0))))");
                    table.ForeignKey(
                        name: "FK_lab_result_revisions_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lab_result_revisions_lab_requests_LabRequestId",
                        column: x => x.LabRequestId,
                        principalTable: "lab_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lab_requests_CancelledByUserId",
                table: "lab_requests",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_requests_CollectedByUserId",
                table: "lab_requests",
                column: "CollectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_requests_CreationToken",
                table: "lab_requests",
                column: "CreationToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lab_requests_LabTestId",
                table: "lab_requests",
                column: "LabTestId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_requests_MedicalRecordId_RequestedAtUtc",
                table: "lab_requests",
                columns: new[] { "MedicalRecordId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_lab_requests_ProcessingByUserId",
                table: "lab_requests",
                column: "ProcessingByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_requests_ReceivedByUserId",
                table: "lab_requests",
                column: "ReceivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_requests_RequestedByUserId",
                table: "lab_requests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_requests_SampleIdentifier",
                table: "lab_requests",
                column: "SampleIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lab_requests_Status_RequestedAtUtc",
                table: "lab_requests",
                columns: new[] { "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_lab_result_revisions_AuthorUserId",
                table: "lab_result_revisions",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_result_revisions_CreationToken",
                table: "lab_result_revisions",
                column: "CreationToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lab_result_revisions_LabRequestId",
                table: "lab_result_revisions",
                column: "LabRequestId",
                unique: true,
                filter: "\"FinalizedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_lab_result_revisions_LabRequestId_Number",
                table: "lab_result_revisions",
                columns: new[] { "LabRequestId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lab_tests_Code",
                table: "lab_tests",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lab_tests_IsActive_Category_Name",
                table: "lab_tests",
                columns: new[] { "IsActive", "Category", "Name" });

            migrationBuilder.Sql("""
                CREATE FUNCTION public.medicare_lab_request_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Laboratory request history cannot be deleted' USING ERRCODE='23514';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 'Requested' THEN
                            RAISE EXCEPTION 'New laboratory requests must start as Requested' USING ERRCODE='23514';
                        END IF;
                        PERFORM 1 FROM public.medical_records r JOIN public.patients p ON p."Id"=r."PatientId"
                            JOIN public.doctors d ON d."Id"=r."DoctorId" JOIN public.appointments a ON a."Id"=r."AppointmentId"
                            WHERE r."Id"=NEW."MedicalRecordId" AND r."AuthorUserId"=NEW."RequestedByUserId"
                            AND d."IdentityUserId"=NEW."RequestedByUserId" AND d."IsActive" AND NOT p."IsArchived"
                            AND a."DoctorId"=d."Id" AND a."PatientId"=p."Id" AND a."Status" IN ('Scheduled','Completed')
                            FOR UPDATE OF r,p,d,a;
                        IF NOT FOUND THEN RAISE EXCEPTION 'Invalid laboratory care relationship' USING ERRCODE='23514'; END IF;
                        PERFORM 1 FROM public.lab_tests t WHERE t."Id"=NEW."LabTestId" AND t."IsActive"
                            AND ROW(t."Code",t."Name",t."Category",t."SampleType",t."ResultKind",t."Unit",t."ReferenceRange")
                            IS NOT DISTINCT FROM ROW(NEW."TestCode",NEW."TestName",NEW."Category",NEW."SampleType",NEW."ResultKind",NEW."Unit",NEW."ReferenceRange") FOR SHARE;
                        IF NOT FOUND THEN RAISE EXCEPTION 'Invalid laboratory catalogue snapshot' USING ERRCODE='23514'; END IF;
                    ELSE
                        IF (to_jsonb(OLD) - ARRAY['Version','Status','SampleIdentifier','CollectedAtUtc','CollectedByUserId','ReceivedAtUtc','ReceivedByUserId','ProcessingAtUtc','ProcessingByUserId','CompletedAtUtc','CancelledAtUtc','CancelledByUserId','CancellationReason'])
                            IS DISTINCT FROM (to_jsonb(NEW) - ARRAY['Version','Status','SampleIdentifier','CollectedAtUtc','CollectedByUserId','ReceivedAtUtc','ReceivedByUserId','ProcessingAtUtc','ProcessingByUserId','CompletedAtUtc','CancelledAtUtc','CancelledByUserId','CancellationReason']) THEN
                            RAISE EXCEPTION 'Laboratory request identity and test snapshot are immutable' USING ERRCODE='23514';
                        END IF;
                        IF OLD."Status" IN ('Completed','Cancelled') AND (to_jsonb(OLD)-'Version') IS DISTINCT FROM (to_jsonb(NEW)-'Version') THEN
                            RAISE EXCEPTION 'Terminal laboratory requests cannot be reopened' USING ERRCODE='23514';
                        END IF;
                        IF NEW."Status" <> OLD."Status" AND NOT (
                            (OLD."Status"='Requested' AND NEW."Status" IN ('SampleCollected','Cancelled')) OR
                            (OLD."Status"='SampleCollected' AND NEW."Status" IN ('InProgress','Cancelled')) OR
                            (OLD."Status"='InProgress' AND NEW."Status" IN ('Completed','Cancelled'))) THEN
                            RAISE EXCEPTION 'Invalid laboratory status transition' USING ERRCODE='23514';
                        END IF;
                        IF (OLD."CollectedAtUtc" IS NOT NULL AND ROW(OLD."SampleIdentifier",OLD."CollectedAtUtc",OLD."CollectedByUserId") IS DISTINCT FROM ROW(NEW."SampleIdentifier",NEW."CollectedAtUtc",NEW."CollectedByUserId")) OR
                            (OLD."ReceivedAtUtc" IS NOT NULL AND ROW(OLD."ReceivedAtUtc",OLD."ReceivedByUserId") IS DISTINCT FROM ROW(NEW."ReceivedAtUtc",NEW."ReceivedByUserId")) OR
                            (OLD."ProcessingAtUtc" IS NOT NULL AND ROW(OLD."ProcessingAtUtc",OLD."ProcessingByUserId") IS DISTINCT FROM ROW(NEW."ProcessingAtUtc",NEW."ProcessingByUserId")) THEN
                            RAISE EXCEPTION 'Recorded sample custody is immutable' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER medicare_lab_request_guard BEFORE INSERT OR UPDATE OR DELETE ON public.lab_requests
                    FOR EACH ROW EXECUTE FUNCTION public.medicare_lab_request_guard();

                CREATE FUNCTION public.medicare_lab_result_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE request_status text; result_kind text; previous_number integer; previous_author text; previous_final timestamptz;
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Laboratory result history cannot be deleted' USING ERRCODE='23514'; END IF;
                    IF TG_OP='UPDATE' THEN
                        IF OLD."FinalizedAtUtc" IS NOT NULL THEN RAISE EXCEPTION 'Finalized laboratory results are immutable' USING ERRCODE='23514'; END IF;
                        IF ROW(NEW."Id",NEW."LabRequestId",NEW."CreationToken",NEW."Number",NEW."AuthorUserId",NEW."CreatedAtUtc") IS DISTINCT FROM
                            ROW(OLD."Id",OLD."LabRequestId",OLD."CreationToken",OLD."Number",OLD."AuthorUserId",OLD."CreatedAtUtc") THEN
                            RAISE EXCEPTION 'Laboratory result identity is immutable' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    SELECT "Status","ResultKind" INTO request_status,result_kind FROM public.lab_requests WHERE "Id"=NEW."LabRequestId" FOR UPDATE;
                    IF NOT FOUND OR request_status NOT IN ('InProgress','Completed') THEN
                        RAISE EXCEPTION 'Results require an active processing or completed request' USING ERRCODE='23514';
                    END IF;
                    IF (result_kind='Numeric' AND NEW."TextValue" IS NOT NULL) OR (result_kind='Text' AND NEW."NumericValue" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Laboratory result format does not match the ordered test' USING ERRCODE='23514';
                    END IF;
                    IF TG_OP='INSERT' THEN
                        SELECT "Number","AuthorUserId","FinalizedAtUtc" INTO previous_number,previous_author,previous_final
                            FROM public.lab_result_revisions WHERE "LabRequestId"=NEW."LabRequestId" ORDER BY "Number" DESC LIMIT 1;
                        IF NEW."Number"<>COALESCE(previous_number,0)+1 OR NEW."FinalizedAtUtc" IS NOT NULL OR
                            (previous_number IS NULL AND request_status<>'InProgress') OR
                            (previous_number IS NOT NULL AND (previous_final IS NULL OR request_status<>'Completed' OR previous_author IS DISTINCT FROM NEW."AuthorUserId")) THEN
                            RAISE EXCEPTION 'A sequential author-owned laboratory draft is required' USING ERRCODE='23514';
                        END IF;
                        PERFORM 1 FROM public."AspNetUserRoles" ur JOIN public."AspNetRoles" role ON role."Id"=ur."RoleId"
                            WHERE ur."UserId"=NEW."AuthorUserId" AND role."Name"='Laboratory Staff';
                        IF NOT FOUND THEN RAISE EXCEPTION 'A laboratory staff result author is required' USING ERRCODE='23514'; END IF;
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER medicare_lab_result_guard BEFORE INSERT OR UPDATE OR DELETE ON public.lab_result_revisions
                    FOR EACH ROW EXECUTE FUNCTION public.medicare_lab_result_guard();

                -- Deferred until transaction end: EF can update the request and finalize the result in either order.
                CREATE FUNCTION public.medicare_lab_completion_check() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE request_id uuid; request_status text; has_final boolean;
                BEGIN
                    IF TG_TABLE_NAME='lab_requests' THEN request_id:=NEW."Id"; ELSE request_id:=NEW."LabRequestId"; END IF;
                    SELECT "Status" INTO request_status FROM public.lab_requests WHERE "Id"=request_id;
                    SELECT EXISTS(SELECT 1 FROM public.lab_result_revisions WHERE "LabRequestId"=request_id AND "FinalizedAtUtc" IS NOT NULL) INTO has_final;
                    IF (request_status='Completed') IS DISTINCT FROM has_final THEN
                        RAISE EXCEPTION 'Completed laboratory requests require finalized results and finalized results require Completed status' USING ERRCODE='23514';
                    END IF;
                    RETURN NULL;
                END; $$;
                CREATE CONSTRAINT TRIGGER medicare_lab_request_completion AFTER INSERT OR UPDATE ON public.lab_requests
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_lab_completion_check();
                CREATE CONSTRAINT TRIGGER medicare_lab_result_completion AFTER INSERT OR UPDATE ON public.lab_result_revisions
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_lab_completion_check();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lab_result_revisions");

            migrationBuilder.DropTable(
                name: "lab_requests");

            migrationBuilder.DropTable(
                name: "lab_tests");
            migrationBuilder.Sql("""
                DROP FUNCTION public.medicare_lab_completion_check();
                DROP FUNCTION public.medicare_lab_result_guard();
                DROP FUNCTION public.medicare_lab_request_guard();
                """);
        }
    }
}
