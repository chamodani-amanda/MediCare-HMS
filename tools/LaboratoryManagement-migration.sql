START TRANSACTION;
CREATE TABLE lab_tests (
    "Id" uuid NOT NULL,
    "Code" character varying(40) NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Category" character varying(80) NOT NULL,
    "Description" character varying(2000),
    "SampleType" character varying(100) NOT NULL,
    "ResultKind" character varying(20) NOT NULL,
    "Unit" character varying(60),
    "ReferenceRange" character varying(300),
    "IsActive" boolean NOT NULL,
    "Version" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_lab_tests" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_lab_tests_kind" CHECK ("ResultKind" IN ('Numeric','Text')),
    CONSTRAINT "CK_lab_tests_required" CHECK ("Code" ~ '^[A-Z0-9][A-Z0-9_.-]{0,39}$' AND length(btrim("Name")) > 0 AND length(btrim("Category")) > 0 AND length(btrim("SampleType")) > 0)
);

CREATE TABLE lab_requests (
    "Id" uuid NOT NULL,
    "CreationToken" uuid NOT NULL,
    "MedicalRecordId" uuid NOT NULL,
    "LabTestId" uuid NOT NULL,
    "RequestedByUserId" character varying(450) NOT NULL,
    "TestCode" character varying(40) NOT NULL,
    "TestName" character varying(160) NOT NULL,
    "Category" character varying(80) NOT NULL,
    "SampleType" character varying(100) NOT NULL,
    "ResultKind" character varying(20) NOT NULL,
    "Unit" character varying(60),
    "ReferenceRange" character varying(300),
    "Status" character varying(24) NOT NULL,
    "SampleIdentifier" character varying(40),
    "RequestedAtUtc" timestamp with time zone NOT NULL,
    "CollectedAtUtc" timestamp with time zone,
    "CollectedByUserId" character varying(450),
    "ReceivedAtUtc" timestamp with time zone,
    "ReceivedByUserId" character varying(450),
    "ProcessingAtUtc" timestamp with time zone,
    "ProcessingByUserId" character varying(450),
    "CompletedAtUtc" timestamp with time zone,
    "CancelledAtUtc" timestamp with time zone,
    "CancelledByUserId" character varying(450),
    "CancellationReason" character varying(1000),
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_lab_requests" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_lab_requests_cancellation" CHECK (("Status" <> 'Cancelled' AND "CancelledAtUtc" IS NULL AND "CancelledByUserId" IS NULL AND "CancellationReason" IS NULL) OR ("Status" = 'Cancelled' AND "CancelledAtUtc" IS NOT NULL AND "CancelledAtUtc" >= "RequestedAtUtc" AND "CancelledByUserId" IS NOT NULL AND length(btrim("CancellationReason")) > 0 AND "CancellationReason" IS NOT NULL)),
    CONSTRAINT "CK_lab_requests_completion" CHECK (("CompletedAtUtc" IS NULL AND "Status" <> 'Completed') OR ("CompletedAtUtc" IS NOT NULL AND "Status" = 'Completed' AND "ProcessingAtUtc" IS NOT NULL AND "CompletedAtUtc" >= "ProcessingAtUtc")),
    CONSTRAINT "CK_lab_requests_kind" CHECK ("ResultKind" IN ('Numeric','Text')),
    CONSTRAINT "CK_lab_requests_processing" CHECK (("ProcessingAtUtc" IS NULL AND "ProcessingByUserId" IS NULL) OR ("ProcessingAtUtc" IS NOT NULL AND "ProcessingByUserId" IS NOT NULL AND "ReceivedAtUtc" IS NOT NULL AND "ProcessingAtUtc" >= "ReceivedAtUtc")),
    CONSTRAINT "CK_lab_requests_receipt" CHECK (("ReceivedAtUtc" IS NULL AND "ReceivedByUserId" IS NULL) OR ("ReceivedAtUtc" IS NOT NULL AND "ReceivedByUserId" IS NOT NULL AND "CollectedAtUtc" IS NOT NULL AND "ReceivedAtUtc" >= "CollectedAtUtc")),
    CONSTRAINT "CK_lab_requests_sample" CHECK (("SampleIdentifier" IS NULL AND "CollectedAtUtc" IS NULL AND "CollectedByUserId" IS NULL) OR ("SampleIdentifier" IS NOT NULL AND "SampleIdentifier" ~ '^LAB-[A-F0-9]{32}$' AND "CollectedAtUtc" >= "RequestedAtUtc" AND "CollectedAtUtc" IS NOT NULL AND "CollectedByUserId" IS NOT NULL)),
    CONSTRAINT "CK_lab_requests_stage" CHECK (("Status" IN ('Requested','Cancelled') OR "CollectedAtUtc" IS NOT NULL) AND ("Status" NOT IN ('InProgress','Completed') OR "ProcessingAtUtc" IS NOT NULL) AND ("Status" <> 'Requested' OR "CollectedAtUtc" IS NULL) AND ("Status" <> 'SampleCollected' OR "ProcessingAtUtc" IS NULL)),
    CONSTRAINT "CK_lab_requests_status" CHECK ("Status" IN ('Requested','SampleCollected','InProgress','Completed','Cancelled')),
    CONSTRAINT "CK_lab_requests_token" CHECK ("CreationToken" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_lab_requests_AspNetUsers_CancelledByUserId" FOREIGN KEY ("CancelledByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_lab_requests_AspNetUsers_CollectedByUserId" FOREIGN KEY ("CollectedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_lab_requests_AspNetUsers_ProcessingByUserId" FOREIGN KEY ("ProcessingByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_lab_requests_AspNetUsers_ReceivedByUserId" FOREIGN KEY ("ReceivedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_lab_requests_AspNetUsers_RequestedByUserId" FOREIGN KEY ("RequestedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_lab_requests_lab_tests_LabTestId" FOREIGN KEY ("LabTestId") REFERENCES lab_tests ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_lab_requests_medical_records_MedicalRecordId" FOREIGN KEY ("MedicalRecordId") REFERENCES medical_records ("Id") ON DELETE RESTRICT
);

CREATE TABLE lab_result_revisions (
    "Id" uuid NOT NULL,
    "LabRequestId" uuid NOT NULL,
    "CreationToken" uuid NOT NULL,
    "Number" integer NOT NULL,
    "AuthorUserId" character varying(450) NOT NULL,
    "NumericValue" numeric(28,8),
    "TextValue" character varying(4000),
    "Comments" character varying(2000),
    "CorrectionReason" character varying(1000),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "FinalizedAtUtc" timestamp with time zone,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_lab_result_revisions" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_lab_result_revisions_number" CHECK ("Number" >= 1 AND (("Number" = 1 AND "CorrectionReason" IS NULL) OR ("Number" > 1 AND "CorrectionReason" IS NOT NULL AND length(btrim("CorrectionReason")) > 0))),
    CONSTRAINT "CK_lab_result_revisions_token" CHECK ("CreationToken" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_lab_result_revisions_value" CHECK (("NumericValue" IS NULL OR "NumericValue" <> 'NaN'::numeric) AND NOT ("NumericValue" IS NOT NULL AND "TextValue" IS NOT NULL) AND ("FinalizedAtUtc" IS NULL OR ("FinalizedAtUtc" >= "CreatedAtUtc" AND ("NumericValue" IS NOT NULL OR ("TextValue" IS NOT NULL AND length(btrim("TextValue")) > 0))))),
    CONSTRAINT "FK_lab_result_revisions_AspNetUsers_AuthorUserId" FOREIGN KEY ("AuthorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_lab_result_revisions_lab_requests_LabRequestId" FOREIGN KEY ("LabRequestId") REFERENCES lab_requests ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_lab_requests_CancelledByUserId" ON lab_requests ("CancelledByUserId");

CREATE INDEX "IX_lab_requests_CollectedByUserId" ON lab_requests ("CollectedByUserId");

CREATE UNIQUE INDEX "IX_lab_requests_CreationToken" ON lab_requests ("CreationToken");

CREATE INDEX "IX_lab_requests_LabTestId" ON lab_requests ("LabTestId");

CREATE INDEX "IX_lab_requests_MedicalRecordId_RequestedAtUtc" ON lab_requests ("MedicalRecordId", "RequestedAtUtc");

CREATE INDEX "IX_lab_requests_ProcessingByUserId" ON lab_requests ("ProcessingByUserId");

CREATE INDEX "IX_lab_requests_ReceivedByUserId" ON lab_requests ("ReceivedByUserId");

CREATE INDEX "IX_lab_requests_RequestedByUserId" ON lab_requests ("RequestedByUserId");

CREATE UNIQUE INDEX "IX_lab_requests_SampleIdentifier" ON lab_requests ("SampleIdentifier");

CREATE INDEX "IX_lab_requests_Status_RequestedAtUtc" ON lab_requests ("Status", "RequestedAtUtc");

CREATE INDEX "IX_lab_result_revisions_AuthorUserId" ON lab_result_revisions ("AuthorUserId");

CREATE UNIQUE INDEX "IX_lab_result_revisions_CreationToken" ON lab_result_revisions ("CreationToken");

CREATE UNIQUE INDEX "IX_lab_result_revisions_LabRequestId" ON lab_result_revisions ("LabRequestId") WHERE "FinalizedAtUtc" IS NULL;

CREATE UNIQUE INDEX "IX_lab_result_revisions_LabRequestId_Number" ON lab_result_revisions ("LabRequestId", "Number");

CREATE UNIQUE INDEX "IX_lab_tests_Code" ON lab_tests ("Code");

CREATE INDEX "IX_lab_tests_IsActive_Category_Name" ON lab_tests ("IsActive", "Category", "Name");

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

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119053856_AddLaboratoryManagement', '10.0.15');

COMMIT;

