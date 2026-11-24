START TRANSACTION;
CREATE TABLE admissions (
    "Id" uuid NOT NULL,
    "CreationToken" uuid NOT NULL,
    "PatientId" uuid NOT NULL,
    "DoctorId" uuid NOT NULL,
    "DepartmentId" uuid NOT NULL,
    "Status" character varying(20) NOT NULL,
    "AdmittedAtUtc" timestamp with time zone NOT NULL,
    "AdmittedByUserId" character varying(450) NOT NULL,
    "ClearedAtUtc" timestamp with time zone,
    "ClearedByUserId" character varying(450),
    "DischargedAtUtc" timestamp with time zone,
    "DischargedByUserId" character varying(450),
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_admissions" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_admissions_state" CHECK (("Status"='Admitted' AND "ClearedAtUtc" IS NULL AND "DischargedAtUtc" IS NULL) OR ("Status"='DischargeReady' AND "ClearedAtUtc" IS NOT NULL AND "DischargedAtUtc" IS NULL) OR ("Status"='Discharged' AND "ClearedAtUtc" IS NOT NULL AND "DischargedAtUtc" IS NOT NULL)),
    CONSTRAINT "CK_admissions_times" CHECK (isfinite("AdmittedAtUtc") AND ("ClearedAtUtc" IS NULL OR (isfinite("ClearedAtUtc") AND "ClearedAtUtc" >= "AdmittedAtUtc")) AND ("DischargedAtUtc" IS NULL OR (isfinite("DischargedAtUtc") AND "DischargedAtUtc" >= "ClearedAtUtc")) AND ("ClearedAtUtc" IS NULL) = ("ClearedByUserId" IS NULL) AND ("DischargedAtUtc" IS NULL) = ("DischargedByUserId" IS NULL)),
    CONSTRAINT "CK_admissions_token" CHECK ("CreationToken" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_admissions_AspNetUsers_AdmittedByUserId" FOREIGN KEY ("AdmittedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_admissions_AspNetUsers_ClearedByUserId" FOREIGN KEY ("ClearedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_admissions_AspNetUsers_DischargedByUserId" FOREIGN KEY ("DischargedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_admissions_departments_DepartmentId" FOREIGN KEY ("DepartmentId") REFERENCES departments ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_admissions_doctors_DoctorId" FOREIGN KEY ("DoctorId") REFERENCES doctors ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_admissions_patients_PatientId" FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT
);

CREATE TABLE outpatient_visits (
    "Id" uuid NOT NULL,
    "CreationToken" uuid NOT NULL,
    "PatientId" uuid NOT NULL,
    "DoctorId" uuid NOT NULL,
    "AppointmentId" uuid,
    "Status" character varying(20) NOT NULL,
    "RegisteredByUserId" character varying(450) NOT NULL,
    "RegisteredAtUtc" timestamp with time zone NOT NULL,
    "StartedAtUtc" timestamp with time zone,
    "CompletedAtUtc" timestamp with time zone,
    "CancelledAtUtc" timestamp with time zone,
    "StartedByUserId" character varying(450),
    "CompletedByUserId" character varying(450),
    "CancelledByUserId" character varying(450),
    "CancellationReason" character varying(500),
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_outpatient_visits" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_outpatient_visits_actors" CHECK (("StartedAtUtc" IS NULL) = ("StartedByUserId" IS NULL) AND ("CompletedAtUtc" IS NULL) = ("CompletedByUserId" IS NULL) AND ("CancelledAtUtc" IS NULL) = ("CancelledByUserId" IS NULL) AND (("Status"='Cancelled' AND "CancellationReason" IS NOT NULL AND length(btrim("CancellationReason")) > 0) OR ("Status"<>'Cancelled' AND "CancellationReason" IS NULL))),
    CONSTRAINT "CK_outpatient_visits_state" CHECK (("Status"='Registered' AND "StartedAtUtc" IS NULL AND "CompletedAtUtc" IS NULL AND "CancelledAtUtc" IS NULL) OR ("Status"='InConsultation' AND "StartedAtUtc" IS NOT NULL AND "CompletedAtUtc" IS NULL AND "CancelledAtUtc" IS NULL) OR ("Status"='Completed' AND "StartedAtUtc" IS NOT NULL AND "CompletedAtUtc" IS NOT NULL AND "CancelledAtUtc" IS NULL) OR ("Status"='Cancelled' AND "StartedAtUtc" IS NULL AND "CompletedAtUtc" IS NULL AND "CancelledAtUtc" IS NOT NULL)),
    CONSTRAINT "CK_outpatient_visits_times" CHECK (isfinite("RegisteredAtUtc") AND ("StartedAtUtc" IS NULL OR (isfinite("StartedAtUtc") AND "StartedAtUtc" >= "RegisteredAtUtc")) AND ("CompletedAtUtc" IS NULL OR (isfinite("CompletedAtUtc") AND "CompletedAtUtc" >= "StartedAtUtc")) AND ("CancelledAtUtc" IS NULL OR (isfinite("CancelledAtUtc") AND "CancelledAtUtc" >= "RegisteredAtUtc"))),
    CONSTRAINT "CK_outpatient_visits_token" CHECK ("CreationToken" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_outpatient_visits_AspNetUsers_CancelledByUserId" FOREIGN KEY ("CancelledByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_outpatient_visits_AspNetUsers_CompletedByUserId" FOREIGN KEY ("CompletedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_outpatient_visits_AspNetUsers_RegisteredByUserId" FOREIGN KEY ("RegisteredByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_outpatient_visits_AspNetUsers_StartedByUserId" FOREIGN KEY ("StartedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_outpatient_visits_appointments_AppointmentId" FOREIGN KEY ("AppointmentId") REFERENCES appointments ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_outpatient_visits_doctors_DoctorId" FOREIGN KEY ("DoctorId") REFERENCES doctors ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_outpatient_visits_patients_PatientId" FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT
);

CREATE TABLE wards (
    "Id" uuid NOT NULL,
    "Code" character varying(40) NOT NULL,
    "Name" character varying(120) NOT NULL,
    "DepartmentId" uuid NOT NULL,
    "IsActive" boolean NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_wards" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_wards_fields" CHECK (length(btrim("Code")) > 0 AND "Code" = upper(btrim("Code")) AND length(btrim("Name")) > 0),
    CONSTRAINT "FK_wards_departments_DepartmentId" FOREIGN KEY ("DepartmentId") REFERENCES departments ("Id") ON DELETE RESTRICT
);

CREATE TABLE admission_clinical_notes (
    "AdmissionId" uuid NOT NULL,
    "AdmissionReason" character varying(4000) NOT NULL,
    "DischargeSummary" character varying(8000),
    "MedicalRecordId" uuid,
    "AuthorUserId" character varying(450) NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_admission_clinical_notes" PRIMARY KEY ("AdmissionId"),
    CONSTRAINT "FK_admission_clinical_notes_AspNetUsers_AuthorUserId" FOREIGN KEY ("AuthorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_admission_clinical_notes_admissions_AdmissionId" FOREIGN KEY ("AdmissionId") REFERENCES admissions ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_admission_clinical_notes_medical_records_MedicalRecordId" FOREIGN KEY ("MedicalRecordId") REFERENCES medical_records ("Id") ON DELETE RESTRICT
);

CREATE TABLE outpatient_clinical_notes (
    "OutpatientVisitId" uuid NOT NULL,
    "MedicalRecordId" uuid,
    "FollowUpInformation" character varying(4000),
    "FollowUpOn" date,
    "AuthorUserId" character varying(450) NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_outpatient_clinical_notes" PRIMARY KEY ("OutpatientVisitId"),
    CONSTRAINT "FK_outpatient_clinical_notes_AspNetUsers_AuthorUserId" FOREIGN KEY ("AuthorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_outpatient_clinical_notes_medical_records_MedicalRecordId" FOREIGN KEY ("MedicalRecordId") REFERENCES medical_records ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_outpatient_clinical_notes_outpatient_visits_OutpatientVisit~" FOREIGN KEY ("OutpatientVisitId") REFERENCES outpatient_visits ("Id") ON DELETE RESTRICT
);

CREATE TABLE beds (
    "Id" uuid NOT NULL,
    "WardId" uuid NOT NULL,
    "Number" character varying(40) NOT NULL,
    "IsOperational" boolean NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_beds" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_beds_number" CHECK (length(btrim("Number")) > 0 AND "Number" = upper(btrim("Number"))),
    CONSTRAINT "FK_beds_wards_WardId" FOREIGN KEY ("WardId") REFERENCES wards ("Id") ON DELETE RESTRICT
);

CREATE TABLE bed_occupancies (
    "Id" uuid NOT NULL,
    "AdmissionId" uuid NOT NULL,
    "BedId" uuid NOT NULL,
    "StartedAtUtc" timestamp with time zone NOT NULL,
    "EndedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_bed_occupancies" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_bed_occupancies_times" CHECK (isfinite("StartedAtUtc") AND ("EndedAtUtc" IS NULL OR (isfinite("EndedAtUtc") AND "EndedAtUtc" >= "StartedAtUtc"))),
    CONSTRAINT "FK_bed_occupancies_admissions_AdmissionId" FOREIGN KEY ("AdmissionId") REFERENCES admissions ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_bed_occupancies_beds_BedId" FOREIGN KEY ("BedId") REFERENCES beds ("Id") ON DELETE RESTRICT
);

CREATE TABLE bed_transfers (
    "Id" uuid NOT NULL,
    "Token" uuid NOT NULL,
    "AdmissionId" uuid NOT NULL,
    "FromBedId" uuid,
    "ToBedId" uuid,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "ActorUserId" character varying(450) NOT NULL,
    CONSTRAINT "PK_bed_transfers" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_bed_transfers_fields" CHECK ("Token" <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite("OccurredAtUtc") AND length(btrim("Reason")) > 0 AND ("FromBedId" IS NOT NULL OR "ToBedId" IS NOT NULL) AND "FromBedId" IS DISTINCT FROM "ToBedId"),
    CONSTRAINT "FK_bed_transfers_AspNetUsers_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_bed_transfers_admissions_AdmissionId" FOREIGN KEY ("AdmissionId") REFERENCES admissions ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_bed_transfers_beds_FromBedId" FOREIGN KEY ("FromBedId") REFERENCES beds ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_bed_transfers_beds_ToBedId" FOREIGN KEY ("ToBedId") REFERENCES beds ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_admission_clinical_notes_AuthorUserId" ON admission_clinical_notes ("AuthorUserId");

CREATE INDEX "IX_admission_clinical_notes_MedicalRecordId" ON admission_clinical_notes ("MedicalRecordId");

CREATE INDEX "IX_admissions_AdmittedByUserId" ON admissions ("AdmittedByUserId");

CREATE INDEX "IX_admissions_ClearedByUserId" ON admissions ("ClearedByUserId");

CREATE UNIQUE INDEX "IX_admissions_CreationToken" ON admissions ("CreationToken");

CREATE INDEX "IX_admissions_DepartmentId_Status" ON admissions ("DepartmentId", "Status");

CREATE INDEX "IX_admissions_DischargedByUserId" ON admissions ("DischargedByUserId");

CREATE INDEX "IX_admissions_DoctorId_AdmittedAtUtc" ON admissions ("DoctorId", "AdmittedAtUtc");

CREATE UNIQUE INDEX "IX_admissions_PatientId" ON admissions ("PatientId") WHERE "Status" <> 'Discharged';

CREATE UNIQUE INDEX "IX_bed_occupancies_AdmissionId" ON bed_occupancies ("AdmissionId") WHERE "EndedAtUtc" IS NULL;

CREATE UNIQUE INDEX "IX_bed_occupancies_BedId" ON bed_occupancies ("BedId") WHERE "EndedAtUtc" IS NULL;

CREATE INDEX "IX_bed_transfers_ActorUserId" ON bed_transfers ("ActorUserId");

CREATE INDEX "IX_bed_transfers_AdmissionId_OccurredAtUtc" ON bed_transfers ("AdmissionId", "OccurredAtUtc");

CREATE INDEX "IX_bed_transfers_FromBedId" ON bed_transfers ("FromBedId");

CREATE INDEX "IX_bed_transfers_ToBedId" ON bed_transfers ("ToBedId");

CREATE UNIQUE INDEX "IX_bed_transfers_Token" ON bed_transfers ("Token");

CREATE UNIQUE INDEX "IX_beds_WardId_Number" ON beds ("WardId", "Number");

CREATE INDEX "IX_outpatient_clinical_notes_AuthorUserId" ON outpatient_clinical_notes ("AuthorUserId");

CREATE INDEX "IX_outpatient_clinical_notes_MedicalRecordId" ON outpatient_clinical_notes ("MedicalRecordId");

CREATE UNIQUE INDEX "IX_outpatient_visits_AppointmentId" ON outpatient_visits ("AppointmentId");

CREATE INDEX "IX_outpatient_visits_CancelledByUserId" ON outpatient_visits ("CancelledByUserId");

CREATE INDEX "IX_outpatient_visits_CompletedByUserId" ON outpatient_visits ("CompletedByUserId");

CREATE UNIQUE INDEX "IX_outpatient_visits_CreationToken" ON outpatient_visits ("CreationToken");

CREATE INDEX "IX_outpatient_visits_DoctorId_Status_RegisteredAtUtc" ON outpatient_visits ("DoctorId", "Status", "RegisteredAtUtc");

CREATE INDEX "IX_outpatient_visits_PatientId_RegisteredAtUtc" ON outpatient_visits ("PatientId", "RegisteredAtUtc");

CREATE INDEX "IX_outpatient_visits_RegisteredByUserId" ON outpatient_visits ("RegisteredByUserId");

CREATE INDEX "IX_outpatient_visits_StartedByUserId" ON outpatient_visits ("StartedByUserId");

CREATE UNIQUE INDEX "IX_wards_Code" ON wards ("Code");

CREATE INDEX "IX_wards_DepartmentId" ON wards ("DepartmentId");

ALTER TABLE public.bed_occupancies ADD CONSTRAINT "EX_bed_occupancies_time"
EXCLUDE USING gist ("BedId" WITH =, tstzrange("StartedAtUtc","EndedAtUtc",'[)') WITH &&);

CREATE FUNCTION public.medicare_stay_role(account text, role_name text) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT EXISTS(SELECT 1 FROM public."AspNetUserRoles" ur JOIN public."AspNetRoles" r ON r."Id"=ur."RoleId" WHERE ur."UserId"=account AND r."Name"=role_name)
$$;

CREATE FUNCTION public.medicare_stay_no_delete() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Hospital stay history is permanent' USING ERRCODE='23514'; END $$;
CREATE TRIGGER medicare_transfers_immutable BEFORE UPDATE OR DELETE ON public.bed_transfers FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_no_delete();

CREATE FUNCTION public.medicare_stay_facility_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Facility history is permanent' USING ERRCODE='23514'; END IF;
 IF TG_TABLE_NAME='wards' THEN
  IF NEW."Code"<>OLD."Code" OR NEW."DepartmentId"<>OLD."DepartmentId" THEN RAISE EXCEPTION 'Ward identity is permanent' USING ERRCODE='23514'; END IF;
  IF NOT NEW."IsActive" AND EXISTS(SELECT 1 FROM public.bed_occupancies o JOIN public.beds b ON b."Id"=o."BedId" WHERE b."WardId"=NEW."Id" AND o."EndedAtUtc" IS NULL) THEN RAISE EXCEPTION 'Occupied ward cannot be deactivated' USING ERRCODE='23514'; END IF;
 ELSE
  IF NEW."Number"<>OLD."Number" OR NEW."WardId"<>OLD."WardId" THEN RAISE EXCEPTION 'Bed identity is permanent' USING ERRCODE='23514'; END IF;
  IF NOT NEW."IsOperational" AND EXISTS(SELECT 1 FROM public.bed_occupancies WHERE "BedId"=NEW."Id" AND "EndedAtUtc" IS NULL) THEN RAISE EXCEPTION 'Occupied bed cannot be deactivated' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_ward_guard BEFORE UPDATE OR DELETE ON public.wards FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_facility_guard();
CREATE TRIGGER medicare_bed_guard BEFORE UPDATE OR DELETE ON public.beds FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_facility_guard();

CREATE FUNCTION public.medicare_admission_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE owner_account text;
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Admission history is permanent' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' THEN
  IF NEW."Status"<>'Admitted' OR NOT public.medicare_stay_role(NEW."AdmittedByUserId",'Receptionist') THEN RAISE EXCEPTION 'Receptionist admission registration required' USING ERRCODE='23514'; END IF;
  PERFORM 1 FROM public.patients p JOIN public.doctors d ON d."Id"=NEW."DoctorId" JOIN public.departments dept ON dept."Id"=d."DepartmentId"
   WHERE p."Id"=NEW."PatientId" AND NOT p."IsArchived" AND d."IsActive" AND dept."IsActive" AND d."DepartmentId"=NEW."DepartmentId" AND d."IdentityUserId" IS NOT NULL
   FOR UPDATE OF p,d,dept;
  IF NOT FOUND THEN RAISE EXCEPTION 'Admission patient or doctor is ineligible' USING ERRCODE='23514'; END IF;
 ELSE
  IF (to_jsonb(NEW)-'Status'-'Version'-'ClearedAtUtc'-'ClearedByUserId'-'DischargedAtUtc'-'DischargedByUserId') IS DISTINCT FROM (to_jsonb(OLD)-'Status'-'Version'-'ClearedAtUtc'-'ClearedByUserId'-'DischargedAtUtc'-'DischargedByUserId') THEN RAISE EXCEPTION 'Admission identity is permanent' USING ERRCODE='23514'; END IF;
  IF OLD."Status"='Discharged' THEN RAISE EXCEPTION 'Discharged admission is immutable' USING ERRCODE='23514'; END IF;
  IF OLD."Status"=NEW."Status" THEN
   IF (to_jsonb(NEW)-'Version') IS DISTINCT FROM (to_jsonb(OLD)-'Version') THEN RAISE EXCEPTION 'Invalid admission transition' USING ERRCODE='23514'; END IF;
  ELSIF OLD."Status"='Admitted' AND NEW."Status"='DischargeReady' THEN
   SELECT "IdentityUserId" INTO owner_account FROM public.doctors WHERE "Id"=NEW."DoctorId" AND "IsActive" FOR UPDATE;
   IF owner_account IS NULL OR owner_account IS DISTINCT FROM NEW."ClearedByUserId" OR NOT public.medicare_stay_role(owner_account,'Doctor') OR NOT EXISTS(SELECT 1 FROM public.admission_clinical_notes WHERE "AdmissionId"=NEW."Id" AND "AuthorUserId"=owner_account AND length(btrim("AdmissionReason"))>0 AND length(btrim("DischargeSummary"))>0) THEN RAISE EXCEPTION 'Treating doctor clinical discharge clearance required' USING ERRCODE='23514'; END IF;
  ELSIF OLD."Status"='DischargeReady' AND NEW."Status"='Discharged' THEN
   IF NEW."ClearedAtUtc" IS DISTINCT FROM OLD."ClearedAtUtc" OR NEW."ClearedByUserId" IS DISTINCT FROM OLD."ClearedByUserId" OR NOT public.medicare_stay_role(NEW."DischargedByUserId",'Receptionist') THEN RAISE EXCEPTION 'Receptionist checkout after preserved clinical clearance required' USING ERRCODE='23514'; END IF;
  ELSE RAISE EXCEPTION 'Invalid admission transition' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_admission_guard BEFORE INSERT OR UPDATE OR DELETE ON public.admissions FOR EACH ROW EXECUTE FUNCTION public.medicare_admission_guard();

CREATE FUNCTION public.medicare_occupancy_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE a public.admissions; department uuid;
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Occupancy history is permanent' USING ERRCODE='23514'; END IF;
 SELECT * INTO a FROM public.admissions WHERE "Id"=NEW."AdmissionId" FOR UPDATE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Admission required' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' THEN
  IF a."Status"<>'Admitted' OR NEW."EndedAtUtc" IS NOT NULL OR NEW."StartedAtUtc"<a."AdmittedAtUtc" THEN RAISE EXCEPTION 'Only admitted patients can receive beds' USING ERRCODE='23514'; END IF;
  SELECT w."DepartmentId" INTO department FROM public.beds b JOIN public.wards w ON w."Id"=b."WardId" JOIN public.departments d ON d."Id"=w."DepartmentId" WHERE b."Id"=NEW."BedId" AND b."IsOperational" AND w."IsActive" AND d."IsActive" FOR UPDATE OF b,w,d;
  IF NOT FOUND OR department<>a."DepartmentId" THEN RAISE EXCEPTION 'Operational available bed in treating department required' USING ERRCODE='23514'; END IF;
 ELSE
  IF OLD."EndedAtUtc" IS NOT NULL OR NEW."EndedAtUtc" IS NULL OR (to_jsonb(NEW)-'EndedAtUtc') IS DISTINCT FROM (to_jsonb(OLD)-'EndedAtUtc') THEN RAISE EXCEPTION 'Occupancy may only be closed once' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_occupancy_guard BEFORE INSERT OR UPDATE OR DELETE ON public.bed_occupancies FOR EACH ROW EXECUTE FUNCTION public.medicare_occupancy_guard();

CREATE FUNCTION public.medicare_transfer_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM 1 FROM public.admissions WHERE "Id"=NEW."AdmissionId" FOR UPDATE;
 IF NOT public.medicare_stay_role(NEW."ActorUserId",'Receptionist') THEN RAISE EXCEPTION 'Receptionist bed allocation required' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_transfer_guard BEFORE INSERT ON public.bed_transfers FOR EACH ROW EXECUTE FUNCTION public.medicare_transfer_guard();

CREATE FUNCTION public.medicare_stay_occupancy_check() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE admission_id uuid; a public.admissions; opens integer; t public.bed_transfers;
BEGIN
 IF TG_TABLE_NAME='admissions' THEN admission_id:=NEW."Id"; ELSE admission_id:=NEW."AdmissionId"; END IF;
 SELECT * INTO a FROM public.admissions WHERE "Id"=admission_id;
 SELECT count(*) INTO opens FROM public.bed_occupancies WHERE "AdmissionId"=admission_id AND "EndedAtUtc" IS NULL;
 IF (a."Status"='Discharged' AND opens<>0) OR (a."Status"<>'Discharged' AND opens<>1) THEN RAISE EXCEPTION 'Admission and current bed allocation must balance' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.bed_occupancies o JOIN public.bed_transfers tr ON tr."AdmissionId"=o."AdmissionId" AND tr."ToBedId"=o."BedId" AND tr."OccurredAtUtc"=o."StartedAtUtc" AND tr."FromBedId" IS NULL WHERE o."AdmissionId"=admission_id AND o."StartedAtUtc"=a."AdmittedAtUtc") THEN RAISE EXCEPTION 'Initial bed assignment history required' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM public.bed_occupancies o WHERE o."AdmissionId"=admission_id AND o."EndedAtUtc" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.bed_transfers tr WHERE tr."AdmissionId"=o."AdmissionId" AND tr."FromBedId"=o."BedId" AND tr."OccurredAtUtc"=o."EndedAtUtc")) THEN RAISE EXCEPTION 'Bed closure transfer history required' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM public.bed_occupancies o WHERE o."AdmissionId"=admission_id AND NOT EXISTS(SELECT 1 FROM public.bed_transfers tr WHERE tr."AdmissionId"=o."AdmissionId" AND tr."ToBedId"=o."BedId" AND tr."OccurredAtUtc"=o."StartedAtUtc")) THEN RAISE EXCEPTION 'Bed opening transfer history required' USING ERRCODE='23514'; END IF;
 FOR t IN SELECT * FROM public.bed_transfers WHERE "AdmissionId"=admission_id LOOP
  IF t."FromBedId" IS NULL THEN
   IF t."OccurredAtUtc"<>a."AdmittedAtUtc" THEN RAISE EXCEPTION 'Invalid initial assignment time' USING ERRCODE='23514'; END IF;
  ELSIF NOT EXISTS(SELECT 1 FROM public.bed_occupancies WHERE "AdmissionId"=admission_id AND "BedId"=t."FromBedId" AND "EndedAtUtc"=t."OccurredAtUtc") THEN RAISE EXCEPTION 'Transfer source must close at the transfer time' USING ERRCODE='23514'; END IF;
  IF t."ToBedId" IS NULL THEN
   IF a."Status"<>'Discharged' OR t."OccurredAtUtc" IS DISTINCT FROM a."DischargedAtUtc" THEN RAISE EXCEPTION 'Discharge checkout and bed release must agree' USING ERRCODE='23514'; END IF;
  ELSIF NOT EXISTS(SELECT 1 FROM public.bed_occupancies WHERE "AdmissionId"=admission_id AND "BedId"=t."ToBedId" AND "StartedAtUtc"=t."OccurredAtUtc") THEN RAISE EXCEPTION 'Transfer destination must open at the transfer time' USING ERRCODE='23514'; END IF;
 END LOOP;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER medicare_admission_bed_check AFTER INSERT OR UPDATE ON public.admissions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_occupancy_check();
CREATE CONSTRAINT TRIGGER medicare_occupancy_history_check AFTER INSERT OR UPDATE ON public.bed_occupancies DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_occupancy_check();
CREATE CONSTRAINT TRIGGER medicare_transfer_history_check AFTER INSERT ON public.bed_transfers DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_occupancy_check();

CREATE FUNCTION public.medicare_outpatient_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE owner_account text;
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Outpatient history is permanent' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' THEN
  IF NEW."Status"<>'Registered' OR NOT public.medicare_stay_role(NEW."RegisteredByUserId",'Receptionist') THEN RAISE EXCEPTION 'Receptionist visit registration required' USING ERRCODE='23514'; END IF;
  PERFORM 1 FROM public.patients p JOIN public.doctors d ON d."Id"=NEW."DoctorId" JOIN public.departments dept ON dept."Id"=d."DepartmentId" WHERE p."Id"=NEW."PatientId" AND NOT p."IsArchived" AND d."IsActive" AND dept."IsActive" AND d."IdentityUserId" IS NOT NULL FOR UPDATE OF p,d,dept;
  IF NOT FOUND THEN RAISE EXCEPTION 'Visit patient or doctor is ineligible' USING ERRCODE='23514'; END IF;
  IF NEW."AppointmentId" IS NOT NULL THEN
   PERFORM 1 FROM public.appointments WHERE "Id"=NEW."AppointmentId" AND "PatientId"=NEW."PatientId" AND "DoctorId"=NEW."DoctorId" AND "Status" IN ('Scheduled','Completed') FOR UPDATE;
   IF NOT FOUND THEN RAISE EXCEPTION 'Matching eligible appointment required' USING ERRCODE='23514'; END IF;
  END IF;
 ELSE
  IF (to_jsonb(NEW)-'Status'-'Version'-'StartedAtUtc'-'StartedByUserId'-'CompletedAtUtc'-'CompletedByUserId'-'CancelledAtUtc'-'CancelledByUserId'-'CancellationReason') IS DISTINCT FROM (to_jsonb(OLD)-'Status'-'Version'-'StartedAtUtc'-'StartedByUserId'-'CompletedAtUtc'-'CompletedByUserId'-'CancelledAtUtc'-'CancelledByUserId'-'CancellationReason') THEN RAISE EXCEPTION 'Visit identity is permanent' USING ERRCODE='23514'; END IF;
  IF OLD."Status" IN ('Completed','Cancelled') THEN RAISE EXCEPTION 'Terminal outpatient history is immutable' USING ERRCODE='23514'; END IF;
  IF OLD."Status"=NEW."Status" THEN
   IF (to_jsonb(NEW)-'Version') IS DISTINCT FROM (to_jsonb(OLD)-'Version') THEN RAISE EXCEPTION 'Invalid visit transition' USING ERRCODE='23514'; END IF;
  ELSIF OLD."Status"='Registered' AND NEW."Status"='Cancelled' THEN
   IF NOT public.medicare_stay_role(NEW."CancelledByUserId",'Receptionist') THEN RAISE EXCEPTION 'Receptionist cancellation required' USING ERRCODE='23514'; END IF;
  ELSIF (OLD."Status"='Registered' AND NEW."Status"='InConsultation') OR (OLD."Status"='InConsultation' AND NEW."Status"='Completed') THEN
   SELECT "IdentityUserId" INTO owner_account FROM public.doctors WHERE "Id"=NEW."DoctorId" AND "IsActive" FOR UPDATE;
   IF owner_account IS NULL OR NOT public.medicare_stay_role(owner_account,'Doctor') OR (NEW."Status"='InConsultation' AND NEW."StartedByUserId" IS DISTINCT FROM owner_account) OR (NEW."Status"='Completed' AND (NEW."CompletedByUserId" IS DISTINCT FROM owner_account OR NEW."StartedAtUtc" IS DISTINCT FROM OLD."StartedAtUtc" OR NEW."StartedByUserId" IS DISTINCT FROM OLD."StartedByUserId")) THEN RAISE EXCEPTION 'Assigned treating doctor transition required' USING ERRCODE='23514'; END IF;
   IF NEW."Status"='InConsultation' AND EXISTS(SELECT 1 FROM public.patients WHERE "Id"=NEW."PatientId" AND "IsArchived") THEN RAISE EXCEPTION 'Archived patient cannot start consultation' USING ERRCODE='23514'; END IF;
  ELSE RAISE EXCEPTION 'Invalid visit transition' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_outpatient_guard BEFORE INSERT OR UPDATE OR DELETE ON public.outpatient_visits FOR EACH ROW EXECUTE FUNCTION public.medicare_outpatient_guard();

CREATE FUNCTION public.medicare_stay_clinical_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE patient uuid; doctor uuid; owner_account text; state text;
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Stay clinical history is permanent' USING ERRCODE='23514'; END IF;
 IF TG_TABLE_NAME='admission_clinical_notes' THEN
  SELECT "PatientId","DoctorId","Status" INTO patient,doctor,state FROM public.admissions WHERE "Id"=NEW."AdmissionId" FOR UPDATE;
  IF state IS DISTINCT FROM 'Admitted' THEN RAISE EXCEPTION 'Cleared admission clinical notes are immutable' USING ERRCODE='23514'; END IF;
  IF length(btrim(NEW."AdmissionReason"))=0 THEN RAISE EXCEPTION 'Clinical admission reason required' USING ERRCODE='23514'; END IF;
  IF TG_OP='UPDATE' AND (NEW."AdmissionId"<>OLD."AdmissionId" OR NEW."AuthorUserId"<>OLD."AuthorUserId") THEN RAISE EXCEPTION 'Clinical note identity is permanent' USING ERRCODE='23514'; END IF;
 ELSE
  SELECT "PatientId","DoctorId","Status" INTO patient,doctor,state FROM public.outpatient_visits WHERE "Id"=NEW."OutpatientVisitId" FOR UPDATE;
  IF state IS DISTINCT FROM 'InConsultation' THEN RAISE EXCEPTION 'Outpatient clinical notes require an active consultation' USING ERRCODE='23514'; END IF;
  IF TG_OP='UPDATE' AND (NEW."OutpatientVisitId"<>OLD."OutpatientVisitId" OR NEW."AuthorUserId"<>OLD."AuthorUserId") THEN RAISE EXCEPTION 'Clinical note identity is permanent' USING ERRCODE='23514'; END IF;
 END IF;
 SELECT "IdentityUserId" INTO owner_account FROM public.doctors WHERE "Id"=doctor AND "IsActive" FOR UPDATE;
 IF owner_account IS NULL OR owner_account IS DISTINCT FROM NEW."AuthorUserId" OR NOT public.medicare_stay_role(owner_account,'Doctor') THEN RAISE EXCEPTION 'Assigned treating doctor clinical author required' USING ERRCODE='23514'; END IF;
 IF NEW."MedicalRecordId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.medical_records mr WHERE mr."Id"=NEW."MedicalRecordId" AND mr."PatientId"=patient AND EXISTS(SELECT 1 FROM public.medical_record_revisions r WHERE r."MedicalRecordId"=mr."Id" AND r."FinalizedAtUtc" IS NOT NULL) AND EXISTS(SELECT 1 FROM public.appointments a JOIN public.doctors d ON d."Id"=a."DoctorId" WHERE a."PatientId"=patient AND a."Status" IN ('Scheduled','Completed') AND d."IsActive" AND d."IdentityUserId"=owner_account)) THEN RAISE EXCEPTION 'Existing authorized finalized EMR care reference required' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_admission_clinical_guard BEFORE INSERT OR UPDATE OR DELETE ON public.admission_clinical_notes FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_clinical_guard();
CREATE TRIGGER medicare_outpatient_clinical_guard BEFORE INSERT OR UPDATE OR DELETE ON public.outpatient_clinical_notes FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_clinical_guard();

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119090005_AddInpatientAndOutpatientManagement', '10.0.15');

COMMIT;

