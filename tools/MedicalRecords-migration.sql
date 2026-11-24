START TRANSACTION;
CREATE TABLE medical_records (
    "Id" uuid NOT NULL,
    "AppointmentId" uuid NOT NULL,
    "PatientId" uuid NOT NULL,
    "DoctorId" uuid NOT NULL,
    "AuthorUserId" character varying(450) NOT NULL,
    "CreationToken" uuid NOT NULL,
    "Version" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_medical_records" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_medical_records_token" CHECK ("CreationToken" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_medical_records_AspNetUsers_AuthorUserId" FOREIGN KEY ("AuthorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_medical_records_appointments_AppointmentId" FOREIGN KEY ("AppointmentId") REFERENCES appointments ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_medical_records_doctors_DoctorId" FOREIGN KEY ("DoctorId") REFERENCES doctors ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_medical_records_patients_PatientId" FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT
);

CREATE TABLE medical_record_revisions (
    "Id" uuid NOT NULL,
    "MedicalRecordId" uuid NOT NULL,
    "Number" integer NOT NULL,
    "AuthorUserId" character varying(450) NOT NULL,
    "Symptoms" character varying(4000) NOT NULL,
    "Observations" character varying(8000) NOT NULL,
    "Diagnosis" character varying(4000) NOT NULL,
    "Treatment" character varying(8000) NOT NULL,
    "AmendmentReason" character varying(1000),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "FinalizedAtUtc" timestamp with time zone,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_medical_record_revisions" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_medical_record_revisions_final" CHECK ("FinalizedAtUtc" IS NULL OR ("FinalizedAtUtc" >= "CreatedAtUtc" AND length(btrim("Symptoms")) > 0 AND length(btrim("Diagnosis")) > 0 AND length(btrim("Treatment")) > 0)),
    CONSTRAINT "CK_medical_record_revisions_number" CHECK ("Number" >= 1 AND (("Number" = 1 AND "AmendmentReason" IS NULL) OR ("Number" > 1 AND length(btrim("AmendmentReason")) > 0 AND "AmendmentReason" IS NOT NULL))),
    CONSTRAINT "FK_medical_record_revisions_AspNetUsers_AuthorUserId" FOREIGN KEY ("AuthorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_medical_record_revisions_medical_records_MedicalRecordId" FOREIGN KEY ("MedicalRecordId") REFERENCES medical_records ("Id") ON DELETE RESTRICT
);

CREATE TABLE medical_prescriptions (
    "Id" uuid NOT NULL,
    "MedicalRecordRevisionId" uuid NOT NULL,
    "Position" integer NOT NULL,
    "Medication" character varying(200) NOT NULL,
    "Dosage" character varying(100) NOT NULL,
    "Route" character varying(100) NOT NULL,
    "Frequency" character varying(200) NOT NULL,
    "Duration" character varying(200) NOT NULL,
    "Instructions" character varying(1000),
    CONSTRAINT "PK_medical_prescriptions" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_medical_prescriptions_fields" CHECK ("Position" BETWEEN 0 AND 19 AND length(btrim("Medication")) > 0 AND length(btrim("Dosage")) > 0 AND length(btrim("Route")) > 0 AND length(btrim("Frequency")) > 0 AND length(btrim("Duration")) > 0),
    CONSTRAINT "FK_medical_prescriptions_medical_record_revisions_MedicalRecor~" FOREIGN KEY ("MedicalRecordRevisionId") REFERENCES medical_record_revisions ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_medical_prescriptions_MedicalRecordRevisionId_Position" ON medical_prescriptions ("MedicalRecordRevisionId", "Position");

CREATE INDEX "IX_medical_record_revisions_AuthorUserId" ON medical_record_revisions ("AuthorUserId");

CREATE UNIQUE INDEX "IX_medical_record_revisions_MedicalRecordId" ON medical_record_revisions ("MedicalRecordId") WHERE "FinalizedAtUtc" IS NULL;

CREATE UNIQUE INDEX "IX_medical_record_revisions_MedicalRecordId_Number" ON medical_record_revisions ("MedicalRecordId", "Number");

CREATE UNIQUE INDEX "IX_medical_records_AppointmentId" ON medical_records ("AppointmentId");

CREATE INDEX "IX_medical_records_AuthorUserId" ON medical_records ("AuthorUserId");

CREATE UNIQUE INDEX "IX_medical_records_CreationToken" ON medical_records ("CreationToken");

CREATE INDEX "IX_medical_records_DoctorId_CreatedAtUtc" ON medical_records ("DoctorId", "CreatedAtUtc");

CREATE INDEX "IX_medical_records_PatientId_CreatedAtUtc" ON medical_records ("PatientId", "CreatedAtUtc");

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

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119043722_AddMedicalRecords', '10.0.15');

COMMIT;

